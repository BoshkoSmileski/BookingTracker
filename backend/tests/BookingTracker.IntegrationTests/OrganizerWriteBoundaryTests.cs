using System.Net;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.IntegrationTests;

/// <summary>
/// AuthorizationBoundaryTests covers the READ boundary plus three
/// writes; this covers every remaining authenticated organizer WRITE, including
/// the ones whose ownership is proved from a nested id rather than from the
/// route's page id.
///
/// Two properties are asserted for every attempt, because either alone is
/// insufficient: the HTTP status (the attempt was refused) AND the victim's
/// database row (nothing was written before the refusal). A handler that
/// mutated and then threw would pass the first and fail the second.
/// </summary>
public class OrganizerWriteBoundaryTests : ApiTestBase
{
    private sealed record Arrangement(
        TestData.Workspace Alice, HttpClient AliceClient,
        TestData.Workspace Bob, HttpClient BobClient,
        Guid AliceExceptionId, Guid AliceOverrideId,
        Guid AliceInstructionId, Guid AliceFieldId,
        Guid AliceSessionId, DateOnly BookedDate, TimeOnly BookedTime);

    /// <summary>
    /// Alice is the victim and owns one of everything an organizer can own;
    /// Bob is the attacker and holds a real signed token of his own.
    /// </summary>
    private async Task<Arrangement> ArrangeAsync()
    {
        var alice = await WithDbAsync(db => TestData.AddWorkspaceAsync(db, email: "alice@example.com", slug: "alice-page"));
        var bob = await WithDbAsync(db => TestData.AddWorkspaceAsync(db, email: "bob@example.com", slug: "bob-page"));
        var aliceClient = ClientFor(alice.Organizer);
        var bobClient = ClientFor(bob.Organizer);

        // Everything below is created through the real endpoints, so the ids
        // under attack are ids the application itself issued.
        var blockedDate = TestData.NextBookableWeekday(60);
        var exception = await ReadJsonAsync(await aliceClient.PostAsync(
            "/api/organizer/availability/exceptions",
            RawJson($$"""{"date":"{{blockedDate:yyyy-MM-dd}}","startTime":null,"endTime":null,"type":"Vacation","reason":"Alice is away"}""")));

        var overrideDate = TestData.NextBookableWeekday(70);
        var @override = await ReadJsonAsync(await aliceClient.PutAsync(
            "/api/organizer/availability/overrides",
            RawJson($$"""{"date":"{{overrideDate:yyyy-MM-dd}}","ranges":[{"start":"13:00:00","end":"17:00:00"}],"note":"Alice half day"}""")));

        var withInstruction = await ReadJsonAsync(await aliceClient.PostAsync(
            $"/api/organizer/booking-pages/{alice.Page.Id}/instructions",
            RawJson("""{"text":"Bring your account number."}""")));
        var instructionId = withInstruction.GetProperty("instructions")[0].GetProperty("id").GetGuid();

        var withField = await ReadJsonAsync(await aliceClient.PostAsync(
            $"/api/organizer/booking-pages/{alice.Page.Id}/form-fields",
            RawJson("""{"label":"Company","type":"ShortText","isRequired":false}""")));
        var fieldId = withField.GetProperty("formFields")[0].GetProperty("id").GetGuid();

        var date = TestData.NextBookableWeekday();
        var time = await BookingFlow.FirstAvailableTimeAsync(Client, "alice-page", date);
        var booking = await BookingFlow.BookAsync(Client, "alice-page", date, time, "Alice's Guest", "guest@example.com");
        var sessionId = booking.GetProperty("id").GetGuid();

        return new Arrangement(
            alice, aliceClient, bob, bobClient,
            exception.GetProperty("id").GetGuid(), @override.GetProperty("id").GetGuid(),
            instructionId, fieldId, sessionId, date, time);
    }

    /// <summary>
    /// BookingQuestion and BookingFormField are EF-owned by BookingPage, so
    /// they have no DbSet of their own and are only reachable through the page
    /// that owns them - which is itself part of why a foreign id cannot be
    /// addressed here.
    /// </summary>
    private async Task<IReadOnlyList<BookingQuestion>> AliceInstructionsAsync(Arrangement s)
        => (await WithDbAsync(db => db.BookingPages.SingleAsync(p => p.Id == s.Alice.Page.Id))).Questions;

    private async Task<IReadOnlyList<BookingFormField>> AliceFormFieldsAsync(Arrangement s)
        => (await WithDbAsync(db => db.BookingPages.SingleAsync(p => p.Id == s.Alice.Page.Id))).FormFields;

    private static void AssertRefused(HttpResponseMessage response, string what)
        => Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
            $"{what}: expected 403 or 404, got {(int)response.StatusCode}.");

    // ---- Page-scoped writes, attacked with the victim's page id -------------

    public static TheoryData<string, string, string?> PageScopedWrites() => new()
    {
        { "PUT",    "details",              """{"title":"Hijacked","description":"x"}""" },
        { "PUT",    "limits",               """{"minNoticeMinutes":9999,"maxBookingWindowDays":1,"maxBookingsPerDay":1}""" },
        { "PUT",    "meeting",              """{"meetingProvider":"GoogleMeet"}""" },
        { "PATCH",  "active",               """{"isActive":false}""" },
        { "POST",   "instructions",         """{"text":"Injected instruction"}""" },
        { "POST",   "form-fields",          """{"label":"Injected","type":"ShortText","isRequired":true}""" },
        { "DELETE", "",                     null },
    };

    [Theory]
    [MemberData(nameof(PageScopedWrites))]
    public async Task AnOrganizerCannotWriteToAnotherOrganizersBookingPage(string method, string segment, string? body)
    {
        var s = await ArrangeAsync();
        var path = $"/api/organizer/booking-pages/{s.Alice.Page.Id}"
                 + (segment.Length == 0 ? "" : $"/{segment}");

        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null) request.Content = RawJson(body);
        var response = await s.BobClient.SendAsync(request);

        AssertRefused(response, $"{method} {path}");

        // Every mutable field of Alice's page, plus the collections the two POST
        // rows would have landed in.
        var page = await WithDbAsync(db => db.BookingPages.SingleAsync(p => p.Id == s.Alice.Page.Id));
        Assert.Equal("Test Meeting", page.Title);
        Assert.True(page.IsActive);
        Assert.Equal(MeetingProviderType.None, page.MeetingProvider);
        Assert.Null(page.MinNoticeMinutes);
        Assert.Single(page.Questions);
        Assert.Single(page.FormFields);
    }

    [Fact]
    public async Task AnOrganizerCannotChangeAnotherOrganizersSchedulingSettings()
    {
        var s = await ArrangeAsync();

        var response = await s.BobClient.PutAsync(
            $"/api/organizer/availability/booking-pages/{s.Alice.Page.Id}/scheduling-settings",
            RawJson("""{"durationMinutes":5,"bufferBeforeMinutes":600,"bufferAfterMinutes":600}"""));

        AssertRefused(response, "scheduling-settings");
        var page = await WithDbAsync(db => db.BookingPages.SingleAsync(p => p.Id == s.Alice.Page.Id));
        Assert.Equal(30, page.DurationMinutes);
        Assert.Equal(0, page.BufferBeforeMinutes);
    }

    // ---- Nested ids: the confused-deputy shape ------------------------------

    [Fact]
    public async Task AnOrganizerCannotDeleteAnotherOrganizersInstructionThroughTheirOwnPage()
    {
        // The dangerous variant of the same delete: Bob names a page he really
        // does own, so OwnershipGuard passes - and supplies Alice's instruction
        // id. Only the aggregate can refuse this.
        var s = await ArrangeAsync();

        var response = await s.BobClient.DeleteAsync(
            $"/api/organizer/booking-pages/{s.Bob.Page.Id}/instructions/{s.AliceInstructionId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(await AliceInstructionsAsync(s), q => q.Id == s.AliceInstructionId);
    }

    [Fact]
    public async Task AnOrganizerCannotDeleteAnotherOrganizersFormFieldThroughTheirOwnPage()
    {
        var s = await ArrangeAsync();

        var response = await s.BobClient.DeleteAsync(
            $"/api/organizer/booking-pages/{s.Bob.Page.Id}/form-fields/{s.AliceFieldId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(await AliceFormFieldsAsync(s), f => f.Id == s.AliceFieldId);
    }

    [Fact]
    public async Task AnOrganizerCannotDeleteAnotherOrganizersInstructionThroughTheVictimsPage()
    {
        var s = await ArrangeAsync();

        var response = await s.BobClient.DeleteAsync(
            $"/api/organizer/booking-pages/{s.Alice.Page.Id}/instructions/{s.AliceInstructionId}");

        AssertRefused(response, "instructions delete via victim page");
        Assert.Contains(await AliceInstructionsAsync(s), q => q.Id == s.AliceInstructionId);
    }

    [Fact]
    public async Task AnOrganizerCannotDeleteAnotherOrganizersFormFieldThroughTheVictimsPage()
    {
        var s = await ArrangeAsync();

        var response = await s.BobClient.DeleteAsync(
            $"/api/organizer/booking-pages/{s.Alice.Page.Id}/form-fields/{s.AliceFieldId}");

        AssertRefused(response, "form-fields delete via victim page");
        Assert.Contains(await AliceFormFieldsAsync(s), f => f.Id == s.AliceFieldId);
    }

    // ---- Organizer-scoped availability rows ---------------------------------

    [Fact]
    public async Task AnOrganizerCannotDeleteAnotherOrganizersAvailabilityOverride()
    {
        var s = await ArrangeAsync();

        var response = await s.BobClient.DeleteAsync($"/api/organizer/availability/overrides/{s.AliceOverrideId}");

        AssertRefused(response, "override delete");
        Assert.Single(await WithDbAsync(db => db.AvailabilityOverrides.Where(o => o.Id == s.AliceOverrideId).ToListAsync()));
    }

    [Fact]
    public async Task AnOrganizerCannotDeleteAnotherOrganizersBlockedPeriod()
    {
        var s = await ArrangeAsync();

        var response = await s.BobClient.DeleteAsync($"/api/organizer/availability/exceptions/{s.AliceExceptionId}");

        AssertRefused(response, "exception delete");
        Assert.Single(await WithDbAsync(db => db.AvailabilityExceptions.Where(e => e.Id == s.AliceExceptionId).ToListAsync()));
    }

    [Fact]
    public async Task SavingAnOverrideNeverTouchesAnotherOrganizersOverrideForTheSameDate()
    {
        // The upsert takes no id at all, so the only way it could cross tenants
        // is by matching on date alone.
        var s = await ArrangeAsync();
        var date = TestData.NextBookableWeekday(70);

        var response = await s.BobClient.PutAsync(
            "/api/organizer/availability/overrides",
            RawJson($$"""{"date":"{{date:yyyy-MM-dd}}","ranges":[],"note":"Bob closes this day"}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var aliceOverride = await WithDbAsync(db => db.AvailabilityOverrides.SingleAsync(o => o.Id == s.AliceOverrideId));
        Assert.Equal(s.Alice.Organizer.Id, aliceOverride.OrganizerId);
        Assert.Single(aliceOverride.Ranges);
        Assert.Equal("Alice half day", aliceOverride.Note);
    }

    // ---- Booking management, and the external side effects it triggers ------

    [Fact]
    public async Task AnOrganizerCannotCancelAnotherOrganizersBooking()
    {
        var s = await ArrangeAsync();
        var queuedBefore = await WithDbAsync(db => db.EmailNotifications.CountAsync());

        // Bob names his OWN page in the route and Alice's session id: the route
        // page id is not what ownership is derived from, so this is the shape
        // that would slip past a guard reading the route instead of the session.
        var response = await s.BobClient.PostAsync(
            $"/api/organizer/booking-pages/{s.Bob.Page.Id}/sessions/{s.AliceSessionId}/cancel",
            RawJson("""{"reason":"Cancelled by an attacker"}"""));

        AssertRefused(response, "cancel");

        var session = await WithDbAsync(db => db.BookingSessions.SingleAsync(x => x.Id == s.AliceSessionId));
        Assert.Equal(BookingSessionStatus.Submitted, session.Status);
        Assert.Null(session.CancelledAt);
        Assert.Null(session.CancellationReason);

        // Authorization must precede every external side effect.
        Assert.Equal(queuedBefore, await WithDbAsync(db => db.EmailNotifications.CountAsync()));
        Assert.Empty(Factory.Calendar.Cancelled);
        Assert.Empty(Factory.Emails.Sent);
    }

    [Fact]
    public async Task AnOrganizerCannotRescheduleAnotherOrganizersBooking()
    {
        var s = await ArrangeAsync();
        var queuedBefore = await WithDbAsync(db => db.EmailNotifications.CountAsync());
        var newDate = TestData.NextBookableWeekday(21);

        var response = await s.BobClient.PostAsync(
            $"/api/organizer/booking-pages/{s.Bob.Page.Id}/sessions/{s.AliceSessionId}/reschedule",
            RawJson($$"""{"newDate":"{{newDate:yyyy-MM-dd}}","newTime":"11:00:00"}"""));

        AssertRefused(response, "reschedule");

        var session = await WithDbAsync(db => db.BookingSessions.SingleAsync(x => x.Id == s.AliceSessionId));
        Assert.Equal(s.BookedDate, session.SelectedDate);
        Assert.Equal(s.BookedTime, session.SelectedTime);
        Assert.Equal(0, session.RescheduleCount);

        Assert.Equal(queuedBefore, await WithDbAsync(db => db.EmailNotifications.CountAsync()));
        Assert.Empty(Factory.Calendar.Rescheduled);
    }

    [Fact]
    public async Task AnOrganizerCannotResendAnotherOrganizersBookingConfirmation()
    {
        var s = await ArrangeAsync();
        var queuedBefore = await WithDbAsync(db => db.EmailNotifications.CountAsync());

        var response = await s.BobClient.PostAsync(
            $"/api/organizer/booking-pages/{s.Bob.Page.Id}/sessions/{s.AliceSessionId}/resend-confirmation",
            content: null);

        AssertRefused(response, "resend-confirmation");
        Assert.Equal(queuedBefore, await WithDbAsync(db => db.EmailNotifications.CountAsync()));
        Assert.Empty(Factory.Emails.Sent);
    }

    // ---- Session reads reachable from the same routes -----------------------

    [Theory]
    [InlineData("")]
    [InlineData("/timeline")]
    [InlineData("/email-history")]
    [InlineData("/reminders")]
    public async Task AnOrganizerCannotReadAnotherOrganizersSessionThroughTheirOwnPage(string suffix)
    {
        var s = await ArrangeAsync();

        var response = await s.BobClient.GetAsync(
            $"/api/organizer/booking-pages/{s.Bob.Page.Id}/sessions/{s.AliceSessionId}{suffix}");

        AssertRefused(response, $"session read {suffix}");
    }

    [Fact]
    public async Task AnOrganizerCannotExportAnotherOrganizersAnalytics()
    {
        var s = await ArrangeAsync();

        foreach (var path in new[] { "bookings", "funnel", "operations", "activity", "export/csv", "export/pdf" })
        {
            var response = await s.BobClient.GetAsync(
                $"/api/organizer/analytics/{path}?bookingPageId={s.Alice.Page.Id}");
            AssertRefused(response, $"analytics {path}");
        }
    }

    // ---- The legitimate half, so "refused" cannot mean "everything refuses" --

    [Fact]
    public async Task AnOrganizerCanStillDoAllOfThisToTheirOwnResources()
    {
        var s = await ArrangeAsync();

        var details = await s.AliceClient.PutAsync(
            $"/api/organizer/booking-pages/{s.Alice.Page.Id}/details",
            RawJson("""{"title":"Renamed by its owner","description":null}"""));
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);

        var instruction = await s.AliceClient.DeleteAsync(
            $"/api/organizer/booking-pages/{s.Alice.Page.Id}/instructions/{s.AliceInstructionId}");
        Assert.Equal(HttpStatusCode.OK, instruction.StatusCode);

        var field = await s.AliceClient.DeleteAsync(
            $"/api/organizer/booking-pages/{s.Alice.Page.Id}/form-fields/{s.AliceFieldId}");
        Assert.Equal(HttpStatusCode.OK, field.StatusCode);

        var blocked = await s.AliceClient.DeleteAsync($"/api/organizer/availability/exceptions/{s.AliceExceptionId}");
        Assert.Equal(HttpStatusCode.NoContent, blocked.StatusCode);

        var @override = await s.AliceClient.DeleteAsync($"/api/organizer/availability/overrides/{s.AliceOverrideId}");
        Assert.Equal(HttpStatusCode.NoContent, @override.StatusCode);

        var cancel = await s.AliceClient.PostAsync(
            $"/api/organizer/booking-pages/{s.Alice.Page.Id}/sessions/{s.AliceSessionId}/cancel",
            RawJson("""{"reason":"Owner cancelled"}"""));
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        var session = await WithDbAsync(db => db.BookingSessions.SingleAsync(x => x.Id == s.AliceSessionId));
        Assert.Equal(BookingSessionStatus.Cancelled, session.Status);
    }

    /// <summary>
    /// The route's page id is not what ownership is derived from for a session -
    /// the session's own BookingPageId is. Pinned so the refusals above are
    /// understood as coming from the session, not from the URL.
    /// </summary>
    [Fact]
    public async Task ASessionRouteIgnoresItsPageIdSegment()
    {
        var s = await ArrangeAsync();

        var response = await s.AliceClient.GetAsync(
            $"/api/organizer/booking-pages/{s.Bob.Page.Id}/sessions/{s.AliceSessionId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(s.AliceSessionId, body.GetProperty("id").GetGuid());
    }
}
