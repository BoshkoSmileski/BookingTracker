using System.Net;
using System.Text.Json;
using BookingTracker.Domain.Enums;
using BookingTracker.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.IntegrationTests;

/// <summary>
/// The guest's self-service screens: view, cancel and reschedule a booking with
/// nothing but a PublicToken.
///
/// The token is the entire credential here, so the boundary worth covering is
/// what happens when it is wrong, and what the DTO behind it is allowed to
/// reveal - a guest reaches these screens holding no account at all.
/// </summary>
public class BookingManagementEndpointTests : ApiTestBase
{
    private const string Slug = "manage-page";

    private sealed record Booked(TestData.Workspace Workspace, string Token, Guid SessionId, DateOnly Date, TimeOnly Time);

    private async Task<Booked> ArrangeBookingAsync(string timeZoneId = "UTC")
    {
        var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(db, slug: Slug, timeZoneId: timeZoneId));
        var date = TestData.NextBookableWeekday();
        var time = await BookingFlow.FirstAvailableTimeAsync(Client, Slug, date);

        await BookingFlow.BookAsync(Client, Slug, date, time, "Jane Doe", "jane@example.com");

        var session = await WithDbAsync(db => db.BookingSessions.SingleAsync());
        return new Booked(workspace, session.PublicToken!, session.Id, date, time);
    }

    // ---- Reading a booking --------------------------------------------------

    [Fact]
    public async Task AGuestCanReadTheirBookingWithTheToken()
    {
        var booked = await ArrangeBookingAsync();

        var response = await Client.GetAsync($"/api/bookings/{booked.Token}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("Jane Doe", body.GetProperty("name").GetString());
        Assert.Equal("Submitted", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("canCancel").GetBoolean());
        Assert.True(body.GetProperty("canReschedule").GetBoolean());
    }

    [Fact]
    public async Task TheBookingCarriesTheFactsTheManageScreenNeeds()
    {
        // Each of these was added because a screen was a dead end without it:
        // the zone names the clock, the organizer's address is how to reach
        // them, and the UTC pair is what Add-to-calendar is built from.
        var booked = await ArrangeBookingAsync(timeZoneId: "Europe/Skopje");

        var body = await ReadJsonAsync(await Client.GetAsync($"/api/bookings/{booked.Token}"));

        Assert.Equal("Europe/Skopje", body.GetProperty("timeZoneId").GetString());
        Assert.Equal("organizer@example.com", body.GetProperty("organizerEmail").GetString());

        var startUtc = body.GetProperty("startUtc").GetDateTime();
        var endUtc = body.GetProperty("endUtc").GetDateTime();
        Assert.Equal(30, (endUtc - startUtc).TotalMinutes);

        // The wall clock the guest is shown and the instant must agree.
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Skopje");
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc), zone);
        Assert.Equal(booked.Time, TimeOnly.FromDateTime(localStart));
    }

    [Fact]
    public async Task AnUnknownTokenIs404AndRevealsNothingAboutTheOrganizer()
    {
        await ArrangeBookingAsync();

        var response = await Client.GetAsync("/api/bookings/completely-made-up-token");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("organizer@example.com", raw);
        Assert.DoesNotContain("Jane Doe", raw);
    }

    // ---- Cancelling ---------------------------------------------------------

    [Fact]
    public async Task AGuestCanCancelWithTheToken()
    {
        var booked = await ArrangeBookingAsync();

        var response = await Client.PostAsync($"/api/bookings/{booked.Token}/cancel",
            RawJson("""{"reason":"Something came up"}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("Cancelled", body.GetProperty("status").GetString());

        var session = await WithDbAsync(db => db.BookingSessions.SingleAsync());
        Assert.Equal(BookingSessionStatus.Cancelled, session.Status);
        Assert.Equal("Something came up", session.CancellationReason);
        Assert.Equal(CancelledByType.Customer, session.CancelledBy);
    }

    [Fact]
    public async Task CancellingFreesTheSlotAgain()
    {
        var booked = await ArrangeBookingAsync();

        await Client.PostAsync($"/api/bookings/{booked.Token}/cancel", RawJson("""{"reason":null}"""));

        var slots = await BookingFlow.GetSlotsAsync(Client, Slug, booked.Date, booked.Date);
        var times = Enumerable.Range(0, slots.GetArrayLength())
            .Select(i => slots[i].GetProperty("localStartTime").GetString()).ToList();

        Assert.Contains(booked.Time.ToString("HH:mm:ss"), times);
    }

    [Fact]
    public async Task CancellingTwiceIsRefused()
    {
        var booked = await ArrangeBookingAsync();
        await Client.PostAsync($"/api/bookings/{booked.Token}/cancel", RawJson("""{"reason":null}"""));

        var response = await Client.PostAsync($"/api/bookings/{booked.Token}/cancel", RawJson("""{"reason":"again"}"""));

        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict,
            $"Expected a second cancellation to be refused, got {(int)response.StatusCode}.");
        var body = await ReadJsonAsync(response);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task ACancelledBookingSaysItCanNoLongerBeChanged()
    {
        var booked = await ArrangeBookingAsync();
        await Client.PostAsync($"/api/bookings/{booked.Token}/cancel", RawJson("""{"reason":null}"""));

        var body = await ReadJsonAsync(await Client.GetAsync($"/api/bookings/{booked.Token}"));

        Assert.Equal("Cancelled", body.GetProperty("status").GetString());
        Assert.False(body.GetProperty("canCancel").GetBoolean());
        Assert.False(body.GetProperty("canReschedule").GetBoolean());
        // Still reachable: the screen has to say how to get in touch.
        Assert.Equal("organizer@example.com", body.GetProperty("organizerEmail").GetString());
    }

    [Fact]
    public async Task CancellingWithAnUnknownTokenIs404AndChangesNothing()
    {
        await ArrangeBookingAsync();

        var response = await Client.PostAsync("/api/bookings/not-a-real-token/cancel", RawJson("""{"reason":null}"""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var session = await WithDbAsync(db => db.BookingSessions.SingleAsync());
        Assert.Equal(BookingSessionStatus.Submitted, session.Status);
    }

    [Fact]
    public async Task AnOversizedCancellationReasonIsA400RatherThanADatabaseError()
    {
        var booked = await ArrangeBookingAsync();
        var reason = new string('x', 2000); // the column limit is 500

        var response = await Client.PostAsync($"/api/bookings/{booked.Token}/cancel",
            RawJson($$"""{"reason":"{{reason}}"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var session = await WithDbAsync(db => db.BookingSessions.SingleAsync());
        Assert.Equal(BookingSessionStatus.Submitted, session.Status);
    }

    // ---- Rescheduling -------------------------------------------------------

    [Fact]
    public async Task AGuestCanRescheduleWithTheToken()
    {
        var booked = await ArrangeBookingAsync();
        var newDate = booked.Date.AddDays(booked.Date.DayOfWeek == DayOfWeek.Friday ? 3 : 1);
        var newTime = await BookingFlow.FirstAvailableTimeAsync(Client, Slug, newDate);

        var response = await Client.PostAsync($"/api/bookings/{booked.Token}/reschedule",
            RawJson($$"""{"newDate":"{{newDate:yyyy-MM-dd}}","newTime":"{{newTime:HH:mm:ss}}"}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var session = await WithDbAsync(db => db.BookingSessions.SingleAsync());
        Assert.Equal(newDate, session.SelectedDate);
        Assert.Equal(newTime, session.SelectedTime);
        Assert.Equal(1, session.RescheduleCount);
        // Still Submitted: a reschedule moves a booking, it does not end it.
        Assert.Equal(BookingSessionStatus.Submitted, session.Status);
    }

    [Fact]
    public async Task ReschedulingKeepsTheSameTokenSoTheLinkStillWorks()
    {
        // The guest's manage link is in an email that was already sent.
        var booked = await ArrangeBookingAsync();
        var newDate = booked.Date.AddDays(booked.Date.DayOfWeek == DayOfWeek.Friday ? 3 : 1);
        var newTime = await BookingFlow.FirstAvailableTimeAsync(Client, Slug, newDate);

        await Client.PostAsync($"/api/bookings/{booked.Token}/reschedule",
            RawJson($$"""{"newDate":"{{newDate:yyyy-MM-dd}}","newTime":"{{newTime:HH:mm:ss}}"}"""));

        var response = await Client.GetAsync($"/api/bookings/{booked.Token}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReschedulingOntoATakenSlotIsRefused()
    {
        var booked = await ArrangeBookingAsync();

        // A second guest takes another slot on the same day.
        var slots = await BookingFlow.GetSlotsAsync(Client, Slug, booked.Date, booked.Date);
        var otherTime = TimeOnly.Parse(slots[0].GetProperty("localStartTime").GetString()!);
        await BookingFlow.BookAsync(Client, Slug, booked.Date, otherTime, "Other", "other@example.com");

        var response = await Client.PostAsync($"/api/bookings/{booked.Token}/reschedule",
            RawJson($$"""{"newDate":"{{booked.Date:yyyy-MM-dd}}","newTime":"{{otherTime:HH:mm:ss}}"}"""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var unchanged = await WithDbAsync(db => db.BookingSessions.SingleAsync(x => x.Id == booked.SessionId));
        Assert.Equal(booked.Time, unchanged.SelectedTime);
    }

    [Fact]
    public async Task ReschedulingACancelledBookingIsRefused()
    {
        var booked = await ArrangeBookingAsync();
        await Client.PostAsync($"/api/bookings/{booked.Token}/cancel", RawJson("""{"reason":null}"""));

        var newDate = booked.Date.AddDays(booked.Date.DayOfWeek == DayOfWeek.Friday ? 3 : 1);
        var response = await Client.PostAsync($"/api/bookings/{booked.Token}/reschedule",
            RawJson($$"""{"newDate":"{{newDate:yyyy-MM-dd}}","newTime":"09:00:00"}"""));

        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict,
            $"Expected rescheduling a cancelled booking to be refused, got {(int)response.StatusCode}.");
    }

    [Fact]
    public async Task ReschedulingWithAMalformedTimeIsA400()
    {
        var booked = await ArrangeBookingAsync();

        var response = await Client.PostAsync($"/api/bookings/{booked.Token}/reschedule",
            RawJson($$"""{"newDate":"{{booked.Date:yyyy-MM-dd}}","newTime":"half past nine"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Organizer-side management -----------------------------------------

    [Fact]
    public async Task TheOrganizerCanCancelTheirOwnBooking()
    {
        var booked = await ArrangeBookingAsync();
        using var organizer = ClientFor(booked.Workspace.Organizer);

        var response = await organizer.PostAsync(
            $"/api/organizer/booking-pages/{booked.Workspace.Page.Id}/sessions/{booked.SessionId}/cancel",
            RawJson("""{"reason":"Organizer had to cancel"}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await WithDbAsync(db => db.BookingSessions.SingleAsync());
        Assert.Equal(CancelledByType.Organizer, session.CancelledBy);
    }

    [Fact]
    public async Task AnotherOrganizerCannotCancelSomeoneElsesBooking()
    {
        var booked = await ArrangeBookingAsync();
        var intruder = await WithDbAsync(db => TestData.AddWorkspaceAsync(db, email: "intruder@example.com", slug: "intruder-page"));
        using var intruderClient = ClientFor(intruder.Organizer);

        var response = await intruderClient.PostAsync(
            $"/api/organizer/booking-pages/{booked.Workspace.Page.Id}/sessions/{booked.SessionId}/cancel",
            RawJson("""{"reason":"not mine"}"""));

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"Expected the cancel to be refused, got {(int)response.StatusCode}.");

        var session = await WithDbAsync(db => db.BookingSessions.SingleAsync(s => s.Id == booked.SessionId));
        Assert.Equal(BookingSessionStatus.Submitted, session.Status);
    }
}
