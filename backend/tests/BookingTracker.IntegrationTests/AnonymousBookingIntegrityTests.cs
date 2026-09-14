using System.Net;
using System.Text;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.IntegrationTests.Infrastructure;

namespace BookingTracker.IntegrationTests;

/// <summary>
/// What a caller constructing HTTP requests by hand - no organizer
/// login, no PublicToken - may and may not do to the public booking flow.
///
/// The threat model is a client that does not use the wizard. Every rule the
/// product enforces on a guest has to be enforced by the server, because the
/// frontend is not a security boundary: it is one of several possible clients,
/// and the only one that cooperates. These tests therefore drive raw HTTP and
/// never the UI.
///
/// The motivating finding is the availability group below. <c>GET /slots</c>
/// applies working hours, blocked dates, date-specific overrides, minimum
/// notice, the booking window, the per-day cap and the page's own active flag;
/// the submit path applied none of them, because
/// <see cref="BookingTracker.Application.Common.BookingConflictChecker"/> asks
/// only whether the chosen time collides with another booking. Measured over
/// HTTP before the fix: every case in
/// <see cref="ASlotThePageDoesNotOfferCannotBeBooked"/> returned 200 while the
/// slot list for the identical date returned an empty array.
/// </summary>
public class AnonymousBookingIntegrityTests : ApiTestBase
{
    private const string Slug = "integrity-page";

    private async Task<TestData.Workspace> ArrangeAsync(int? maxBookingsPerDay = null)
    {
        TestData.Workspace workspace = default!;
        await WithDbAsync(async db =>
            workspace = await TestData.AddWorkspaceAsync(db, slug: Slug, maxBookingsPerDay: maxBookingsPerDay));
        return workspace;
    }

    /// <summary>Start, fill and submit in one go, returning the submit response un-asserted.</summary>
    private async Task<HttpResponseMessage> AttemptBookingAsync(DateOnly date, TimeOnly time)
    {
        var sessionId = await BookingFlow.StartSessionAsync(Client, Slug);
        await BookingFlow.FillAsync(Client, sessionId, date, time);
        return await BookingFlow.SubmitAsync(Client, sessionId);
    }

    private Task<int> SubmittedCountAsync() => WithDbAsync(async db =>
        db.BookingSessions.Count(s => s.Status == BookingSessionStatus.Submitted));

    /// <summary>A weekday in the past - refused for being past, not for being closed.</summary>
    private static DateOnly PreviousWeekday(int daysBack = 30)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-daysBack);
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) date = date.AddDays(-1);
        return date;
    }

    // ---------------------------------------------------------------- availability

    public static TheoryData<string> UnofferedSlots => new()
    {
        "closed-day",     // Saturday, on a Mon-Fri schedule
        "outside-hours",  // 03:00, on a 09:00-17:00 schedule
        "past",           // a weekday that has already gone
        "off-grid",       // 10:07, between two real 15-minute boundaries
    };

    [Theory]
    [MemberData(nameof(UnofferedSlots))]
    public async Task ASlotThePageDoesNotOfferCannotBeBooked(string kind)
    {
        await ArrangeAsync();

        var (date, time) = kind switch
        {
            "closed-day" => (TestData.NextClosedSaturday(), new TimeOnly(10, 0)),
            "outside-hours" => (TestData.NextBookableWeekday(), new TimeOnly(3, 0)),
            "past" => (PreviousWeekday(), new TimeOnly(10, 0)),
            "off-grid" => (TestData.NextBookableWeekday(), new TimeOnly(10, 7)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        // The slot list and the submit path must agree about this date/time.
        var offered = await BookingFlow.GetSlotsAsync(Client, Slug, date, date);
        Assert.DoesNotContain(
            offered.EnumerateArray(),
            slot => TimeOnly.Parse(slot.GetProperty("localStartTime").GetString()!) == time);

        var response = await AttemptBookingAsync(date, time);

        // 409 rather than 400, and with the conflict check's own message: the
        // wizard's "Choose another time" recovery keys on 409, so a different
        // status would produce a correct refusal the one screen designed for it
        // never shows.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await SubmittedCountAsync());
    }

    [Fact]
    public async Task ABlockedDateCannotBeBooked()
    {
        var workspace = await ArrangeAsync();
        var blocked = TestData.NextBookableWeekday(21);

        await WithDbAsync(async db =>
        {
            db.AvailabilityExceptions.Add(AvailabilityException.Create(
                workspace.Organizer.Id, blocked, null, null, AvailabilityExceptionType.Vacation, "Away"));
            await db.SaveChangesAsync();
        });

        var response = await AttemptBookingAsync(blocked, new TimeOnly(10, 0));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await SubmittedCountAsync());
    }

    [Fact]
    public async Task TheBookingWindowIsEnforcedAtSubmitAndNotOnlyInTheSlotList()
    {
        var workspace = await ArrangeAsync();
        await WithDbAsync(async db =>
        {
            var page = await db.BookingPages.FindAsync(workspace.Page.Id);
            page!.UpdateLimits(minNoticeMinutes: null, maxBookingWindowDays: 7, maxBookingsPerDay: null);
            await db.SaveChangesAsync();
        });

        var beyondWindow = TestData.NextBookableWeekday(60);

        Assert.Equal(0, (await BookingFlow.GetSlotsAsync(Client, Slug, beyondWindow, beyondWindow)).GetArrayLength());
        Assert.Equal(HttpStatusCode.Conflict, (await AttemptBookingAsync(beyondWindow, new TimeOnly(10, 0))).StatusCode);
        Assert.Equal(0, await SubmittedCountAsync());
    }

    [Fact]
    public async Task ThePerDayCapCannotBeExceededBySubmittingDirectly()
    {
        await ArrangeAsync(maxBookingsPerDay: 1);
        var day = TestData.NextBookableWeekday();

        Assert.True((await AttemptBookingAsync(day, new TimeOnly(9, 0))).IsSuccessStatusCode);

        // A different, otherwise perfectly valid time on the same day: nothing
        // collides, so only the cap can refuse it.
        Assert.Equal(HttpStatusCode.Conflict, (await AttemptBookingAsync(day, new TimeOnly(14, 0))).StatusCode);
        Assert.Equal(1, await SubmittedCountAsync());
    }

    [Fact]
    public async Task ADeactivatedPageStopsAcceptingASessionThatWasAlreadyInFlight()
    {
        var workspace = await ArrangeAsync();
        var day = TestData.NextBookableWeekday();

        var sessionId = await BookingFlow.StartSessionAsync(Client, Slug);
        await BookingFlow.FillAsync(Client, sessionId, day, new TimeOnly(10, 0));

        await WithDbAsync(async db =>
        {
            var page = await db.BookingPages.FindAsync(workspace.Page.Id);
            page!.Deactivate();
            await db.SaveChangesAsync();
        });

        // 404 rather than 409, because that is what every other public read of
        // an inactive page already answers - the page is not merely full, it is
        // no longer published.
        Assert.Equal(HttpStatusCode.NotFound, (await BookingFlow.SubmitAsync(Client, sessionId)).StatusCode);
        Assert.Equal(0, await SubmittedCountAsync());
    }

    [Fact]
    public async Task EveryOfferedSlotIsStillBookable()
    {
        // The counterweight to the group above: a guard that refuses genuine
        // slots would be a worse defect than the one it fixes, so the slot the
        // API itself offers has to go through.
        await ArrangeAsync();
        var day = TestData.NextBookableWeekday();
        var offered = await BookingFlow.FirstAvailableTimeAsync(Client, Slug, day);

        var response = await AttemptBookingAsync(day, offered);

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal(1, await SubmittedCountAsync());
    }

    // ---------------------------------------------------------------- reschedule

    [Fact]
    public async Task APublicTokenHolderCannotRescheduleOntoASlotThePageDoesNotOffer()
    {
        await ArrangeAsync();
        var day = TestData.NextBookableWeekday();
        var booking = await BookingFlow.BookAsync(Client, Slug, day, await BookingFlow.FirstAvailableTimeAsync(Client, Slug, day));
        var token = booking.GetProperty("publicToken").GetString();

        var saturday = TestData.NextClosedSaturday();
        var response = await Client.PostAsync($"/api/bookings/{token}/reschedule",
            RawJson($$"""{"newDate":"{{saturday:yyyy-MM-dd}}","newTime":"10:00:00"}"""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var unchanged = await WithDbAsync(async db =>
            db.BookingSessions.Single(s => s.PublicToken == token).SelectedDate);
        Assert.Equal(day, unchanged);
    }

    [Fact]
    public async Task AnOrganizerMayStillRescheduleOutsideTheirOwnPublishedHours()
    {
        // The deliberate asymmetry. Published hours say what strangers may take,
        // not what the calendar's owner may schedule - so the guard above is
        // scoped to the anonymous path, and this pins that it stayed there.
        var workspace = await ArrangeAsync();
        var day = TestData.NextBookableWeekday();
        var booking = await BookingFlow.BookAsync(Client, Slug, day, await BookingFlow.FirstAvailableTimeAsync(Client, Slug, day));
        var sessionId = booking.GetProperty("id").GetGuid();

        using var organizer = ClientFor(workspace.Organizer);
        var saturday = TestData.NextClosedSaturday();
        var response = await organizer.PostAsync(
            $"/api/organizer/booking-pages/{workspace.Page.Id}/sessions/{sessionId}/reschedule",
            RawJson($$"""{"newDate":"{{saturday:yyyy-MM-dd}}","newTime":"10:00:00"}"""));

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal(saturday, await WithDbAsync(async db =>
            db.BookingSessions.Single(s => s.Id == sessionId).SelectedDate));
    }

    // ---------------------------------------------------------------- event forging

    public static TheoryData<string> ServerOnlyEventTypes =>
        ["SessionStarted", "BookingSubmitted", "BookingAbandoned", "BookingCancelled",
         "BookingRescheduled", "ReminderSent", "EmailSent", "MeetingLinkAssigned"];

    [Theory]
    [MemberData(nameof(ServerOnlyEventTypes))]
    public async Task LifecycleEventTypesCannotBeForgedThroughTheBatchEndpoint(string eventType)
    {
        // The batch endpoint's switch is an allowlist, not a dispatcher: only
        // the six a visitor can actually cause are reachable. Without that, an
        // anonymous caller could append a BookingSubmitted carrying a
        // PublicToken of their choosing, or a BookingCancelled, straight into
        // the log the projection is rebuilt from.
        await ArrangeAsync();
        var sessionId = await BookingFlow.StartSessionAsync(Client, Slug);

        var response = await BookingFlow.AppendEventsAsync(Client, sessionId,
            new BookingFlow.ClientEvent(eventType, "forged", "forged", 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await WithDbAsync(async db =>
            db.BookingSessionEvents.Count(e => e.SessionId == sessionId && e.EventType != BookingEventType.SessionStarted)));
    }

    [Theory]
    [InlineData("DateSelected", "not-a-date")]
    [InlineData("dateselected", "not-a-date")]
    [InlineData("TimeSelected", "99:99")]
    [InlineData("timeselected", "99:99")]
    public async Task AMalformedDateOrTimeIsRejectedWhateverTheEventTypesCasing(string eventType, string value)
    {
        // The handler parses the event type case-insensitively while the
        // validator used to compare it to the enum's name with ==, so a
        // lower-cased spelling skipped the only rule guarding DateOnly.Parse
        // and produced an unhandled FormatException - an opaque 500 for the
        // exact input this validator exists to answer with a 400. Both
        // spellings are pinned so the two can never disagree again.
        await ArrangeAsync();
        var sessionId = await BookingFlow.StartSessionAsync(Client, Slug);

        var response = await Client.PostAsync($"/api/booking-sessions/{sessionId}/events",
            RawJson($$"""[{"eventType":"{{eventType}}","fieldName":null,"newValue":"{{value}}","clientSequenceNumber":1}]"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnUnknownFieldNameIsRejected()
    {
        await ArrangeAsync();
        var sessionId = await BookingFlow.StartSessionAsync(Client, Slug);

        var response = await BookingFlow.AppendEventsAsync(Client, sessionId,
            new BookingFlow.ClientEvent("FieldChanged", "PublicToken", "attacker-chosen", 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------------------------------------------------------------- state machine

    [Fact]
    public async Task ASubmittedBookingRefusesEveryEventThatWouldChangeIt()
    {
        await ArrangeAsync();
        var day = TestData.NextBookableWeekday();
        var booking = await BookingFlow.BookAsync(Client, Slug, day, await BookingFlow.FirstAvailableTimeAsync(Client, Slug, day));
        var sessionId = booking.GetProperty("id").GetGuid();

        foreach (var forged in new[]
        {
            new BookingFlow.ClientEvent("FieldChanged", "Email", "attacker@evil.test", 50),
            new BookingFlow.ClientEvent("FieldChanged", "Name", "Mallory", 51),
            new BookingFlow.ClientEvent("DateSelected", null, day.AddDays(1).ToString("yyyy-MM-dd"), 52),
            new BookingFlow.ClientEvent("TimeSelected", null, "16:00", 53),
        })
        {
            Assert.Equal(HttpStatusCode.BadRequest,
                (await BookingFlow.AppendEventsAsync(Client, sessionId, forged)).StatusCode);
        }

        // The beacon variant is the same command behind a different content
        // type, so it must refuse identically - otherwise it is a way around
        // the state machine rather than a way to flush a closing tab.
        var beacon = await Client.PostAsync($"/api/booking-sessions/{sessionId}/events/beacon",
            new StringContent(
                """[{"eventType":"FieldChanged","fieldName":"Email","newValue":"attacker@evil.test","clientSequenceNumber":54}]""",
                Encoding.UTF8, "text/plain"));
        Assert.Equal(HttpStatusCode.BadRequest, beacon.StatusCode);

        var session = await WithDbAsync(async db => db.BookingSessions.Single(s => s.Id == sessionId));
        Assert.Equal("jane@example.com", session.Email);
        Assert.Equal("Jane Doe", session.Name);
        Assert.Equal(day, session.SelectedDate);
    }

    [Fact]
    public async Task ASubmittedBookingStillAcceptsAuditOnlyEventsAndIsUnchangedByThem()
    {
        // UserActive/UserInactive/BrowserClosed carry no state, which is why
        // they are the three the aggregate lets through after submit - a guest
        // whose tab is still open goes on generating them. Pinned so that
        // "accepted" is never mistaken for "able to change the booking".
        await ArrangeAsync();
        var day = TestData.NextBookableWeekday();
        var booking = await BookingFlow.BookAsync(Client, Slug, day, await BookingFlow.FirstAvailableTimeAsync(Client, Slug, day));
        var sessionId = booking.GetProperty("id").GetGuid();
        var before = await WithDbAsync(async db => db.BookingSessions.Single(s => s.Id == sessionId));

        foreach (var audit in new[] { "UserActive", "UserInactive", "BrowserClosed" })
        {
            Assert.True((await BookingFlow.AppendEventsAsync(Client, sessionId,
                new BookingFlow.ClientEvent(audit, null, null, 60))).IsSuccessStatusCode);
        }

        var after = await WithDbAsync(async db => db.BookingSessions.Single(s => s.Id == sessionId));
        Assert.Equal(BookingSessionStatus.Submitted, after.Status);
        Assert.Equal(before.SelectedDate, after.SelectedDate);
        Assert.Equal(before.SelectedTime, after.SelectedTime);
        Assert.Equal(before.PublicToken, after.PublicToken);
        Assert.Equal(before.BookingReference, after.BookingReference);
    }

    // ---------------------------------------------------------------- submit integrity

    [Fact]
    public async Task SubmitRefusesASessionThatNeverEstablishedItsPrerequisites()
    {
        await ArrangeAsync();

        var empty = await BookingFlow.StartSessionAsync(Client, Slug);
        Assert.Equal(HttpStatusCode.BadRequest, (await BookingFlow.SubmitAsync(Client, empty)).StatusCode);

        var nameOnly = await BookingFlow.StartSessionAsync(Client, Slug);
        await BookingFlow.AppendEventsAsync(Client, nameOnly, new BookingFlow.ClientEvent("FieldChanged", "Name", "Jane", 1));
        Assert.Equal(HttpStatusCode.BadRequest, (await BookingFlow.SubmitAsync(Client, nameOnly)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await BookingFlow.SubmitAsync(Client, Guid.NewGuid())).StatusCode);
        Assert.Equal(0, await SubmittedCountAsync());
    }

    [Fact]
    public async Task ASecondSubmitProducesNeitherASecondBookingNorAFreshToken()
    {
        await ArrangeAsync();
        var day = TestData.NextBookableWeekday();
        var sessionId = await BookingFlow.StartSessionAsync(Client, Slug);
        await BookingFlow.FillAsync(Client, sessionId, day, await BookingFlow.FirstAvailableTimeAsync(Client, Slug, day));

        var first = await BookingFlow.SubmitAsync(Client, sessionId);
        first.EnsureSuccessStatusCode();
        var token = (await ReadJsonAsync(first)).GetProperty("publicToken").GetString();

        // 400, not 409: the booking succeeded, so telling a double-clicking
        // guest to "choose another time" would send them back to redo work that
        // is already done.
        Assert.Equal(HttpStatusCode.BadRequest, (await BookingFlow.SubmitAsync(Client, sessionId, 100)).StatusCode);

        Assert.Equal(1, await SubmittedCountAsync());
        Assert.Equal(token, await WithDbAsync(async db => db.BookingSessions.Single(s => s.Id == sessionId).PublicToken));
    }

    // ---------------------------------------------------------------- cross-tenant

    [Fact]
    public async Task ASessionIsAlwaysRecordedAgainstThePageItWasStartedOn()
    {
        // Nothing a client sends names a booking page after StartSession: the
        // slug is resolved once, server-side, and every event and the booking
        // itself inherit BookingPageId from the session. So there is no field
        // for a caller to point at somebody else's page.
        var a = await ArrangeAsync();
        var day = TestData.NextBookableWeekday();

        Guid pageB = Guid.Empty;
        await WithDbAsync(async db =>
            pageB = (await TestData.AddWorkspaceAsync(db, email: "b@example.com", slug: "page-b")).Page.Id);

        var sessionId = await BookingFlow.StartSessionAsync(Client, Slug);
        await BookingFlow.FillAsync(Client, sessionId, day, await BookingFlow.FirstAvailableTimeAsync(Client, Slug, day));
        (await BookingFlow.SubmitAsync(Client, sessionId)).EnsureSuccessStatusCode();

        await WithDbAsync(async db =>
        {
            Assert.Equal(a.Page.Id, db.BookingSessions.Single(s => s.Id == sessionId).BookingPageId);
            var events = db.BookingSessionEvents.Where(e => e.SessionId == sessionId).ToList();
            Assert.NotEmpty(events);
            Assert.All(events, e => Assert.Equal(a.Page.Id, e.BookingPageId));
            Assert.DoesNotContain(events, e => e.BookingPageId == pageB);
        });
    }

    [Fact]
    public async Task ATokenOnlyEverResolvesToItsOwnBooking()
    {
        // Two tenants, two bookings, two tokens: submitting one must never
        // yield a credential that reaches the other. The bearer contract Phases
        // 8H-8J audited for disclosure, checked here from the write side.
        await ArrangeAsync();
        await WithDbAsync(async db => await TestData.AddWorkspaceAsync(db, email: "b@example.com", slug: "page-b"));

        var day = TestData.NextBookableWeekday();
        var bookingA = await BookingFlow.BookAsync(Client, Slug, day, await BookingFlow.FirstAvailableTimeAsync(Client, Slug, day), email: "a@example.com");
        var bookingB = await BookingFlow.BookAsync(Client, "page-b", day, await BookingFlow.FirstAvailableTimeAsync(Client, "page-b", day), email: "b-guest@example.com");

        var tokenA = bookingA.GetProperty("publicToken").GetString();
        var tokenB = bookingB.GetProperty("publicToken").GetString();
        Assert.NotEqual(tokenA, tokenB);

        var resolvedA = await ReadJsonAsync(await Client.GetAsync($"/api/bookings/{tokenA}"));
        Assert.Equal("a@example.com", resolvedA.GetProperty("email").GetString());

        var resolvedB = await ReadJsonAsync(await Client.GetAsync($"/api/bookings/{tokenB}"));
        Assert.Equal("b-guest@example.com", resolvedB.GetProperty("email").GetString());
    }
}
