using System.Net;
using System.Text.Json;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.IntegrationTests;

/// <summary>
/// The public booking flow end to end over HTTP: read the page, read the slots,
/// start a session, report interactions, submit.
///
/// This is the one journey with no authentication anywhere in it and the only
/// one an organizer's own customers use, so what is pinned here is the wire
/// contract a browser depends on - field names, value shapes, status codes -
/// rather than the availability rules, which SlotGenerationServiceTests already
/// covers exhaustively and far more cheaply.
/// </summary>
public class PublicBookingLifecycleTests : ApiTestBase
{
    private const string Slug = "public-page";

    private Task<TestData.Workspace> ArrangeAsync(string timeZoneId = "UTC", int? maxBookingsPerDay = null)
        => WithDbAsync(db => TestData.AddWorkspaceAsync(
            db, slug: Slug, timeZoneId: timeZoneId, maxBookingsPerDay: maxBookingsPerDay));

    // ---- Availability -------------------------------------------------------

    [Fact]
    public async Task ThePublicPageIsReadableAnonymously()
    {
        var workspace = await ArrangeAsync();

        var response = await Client.GetAsync($"/api/booking-pages/{Slug}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await ReadJsonAsync(response);
        Assert.Equal(workspace.Page.Id.ToString(), page.GetProperty("id").GetString());
        Assert.Equal("Test Meeting", page.GetProperty("title").GetString());
        Assert.Equal(30, page.GetProperty("durationMinutes").GetInt32());
    }

    [Fact]
    public async Task ThePublicPageCarriesTheOrganizersTimeZone()
    {
        // Every time this API hands a visitor is on the ORGANIZER's clock, so
        // the wizard has to be able to name the zone. Missing it is what let the
        // old wizard show one time and book another.
        await ArrangeAsync(timeZoneId: "Europe/Skopje");

        var page = await ReadJsonAsync(await Client.GetAsync($"/api/booking-pages/{Slug}"));

        Assert.Equal("Europe/Skopje", page.GetProperty("timeZoneId").GetString());
    }

    [Fact]
    public async Task AnUnknownSlugIsA404WithTheApisOwnErrorShape()
    {
        var response = await Client.GetAsync("/api/booking-pages/no-such-page");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.True(body.TryGetProperty("title", out var title));
        Assert.False(string.IsNullOrWhiteSpace(title.GetString()));
    }

    [Fact]
    public async Task SlotsAreReturnedForAWorkingDay()
    {
        await ArrangeAsync();
        var date = TestData.NextBookableWeekday();

        var slots = await BookingFlow.GetSlotsAsync(Client, Slug, date, date);

        Assert.True(slots.GetArrayLength() > 0);
        var first = slots[0];
        // 09:00-17:00 in the seeded schedule, read on the organizer's clock.
        Assert.Equal("09:00:00", first.GetProperty("localStartTime").GetString());
        Assert.Equal("09:30:00", first.GetProperty("localEndTime").GetString());
    }

    [Fact]
    public async Task ASlotCarriesBothTheWallClockAndTheRealInstant()
    {
        // Both readings, because the screen shows one and the .ics is built from
        // the other. In Europe/Skopje (UTC+2 in summer, +1 in winter) they differ.
        await ArrangeAsync(timeZoneId: "Europe/Skopje");
        var date = TestData.NextBookableWeekday();

        var slots = await BookingFlow.GetSlotsAsync(Client, Slug, date, date);
        var first = slots[0];

        Assert.Equal("09:00:00", first.GetProperty("localStartTime").GetString());

        var startUtc = first.GetProperty("startUtc").GetDateTime();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Skopje");
        var backInZone = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(startUtc, DateTimeKind.Utc), zone);

        // The pair a guest is shown and the instant a calendar is built from must
        // describe the same moment.
        Assert.Equal(new DateTime(date.Year, date.Month, date.Day, 9, 0, 0), backInZone);
    }

    [Fact]
    public async Task AClosedWeekendDayOffersNoSlots()
    {
        await ArrangeAsync();
        var saturday = TestData.NextClosedSaturday();

        var slots = await BookingFlow.GetSlotsAsync(Client, Slug, saturday, saturday);

        Assert.Equal(0, slots.GetArrayLength());
    }

    [Fact]
    public async Task ABlockedDateRemovesItsSlots()
    {
        // The endpoint honouring an availability exception, not the maths behind
        // it - that lives in the unit suite.
        var workspace = await ArrangeAsync();
        var date = TestData.NextBookableWeekday();

        await WithDbAsync(async db =>
        {
            db.AvailabilityExceptions.Add(AvailabilityException.Create(
                workspace.Organizer.Id, date, null, null, AvailabilityExceptionType.Vacation, "Away"));
            await db.SaveChangesAsync();
        });

        var slots = await BookingFlow.GetSlotsAsync(Client, Slug, date, date);

        Assert.Equal(0, slots.GetArrayLength());
    }

    [Fact]
    public async Task ADisabledPageIsNotBookable()
    {
        var workspace = await ArrangeAsync();
        await WithDbAsync(async db =>
        {
            var page = await db.BookingPages.SingleAsync(p => p.Id == workspace.Page.Id);
            page.Deactivate();
            await db.SaveChangesAsync();
        });

        var response = await Client.GetAsync($"/api/booking-pages/{Slug}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Booking ------------------------------------------------------------

    [Fact]
    public async Task AGuestCanBookASlotAnonymouslyAndItIsPersisted()
    {
        var workspace = await ArrangeAsync();
        var date = TestData.NextBookableWeekday();
        var time = await BookingFlow.FirstAvailableTimeAsync(Client, Slug, date);

        var confirmation = await BookingFlow.BookAsync(Client, Slug, date, time, "Jane Doe", "jane@example.com");

        Assert.Equal("Submitted", confirmation.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(confirmation.GetProperty("bookingReference").GetString()));

        var session = await WithDbAsync(db => db.BookingSessions.SingleAsync());
        Assert.Equal(BookingSessionStatus.Submitted, session.Status);
        Assert.Equal("Jane Doe", session.Name);
        Assert.Equal("jane@example.com", session.Email);
        Assert.Equal(date, session.SelectedDate);
        Assert.Equal(time, session.SelectedTime);
        // The booking belongs to the page it was made on, and so to its organizer.
        Assert.Equal(workspace.Page.Id, session.BookingPageId);
    }

    [Fact]
    public async Task ABookedSlotDisappearsFromTheAvailableSlots()
    {
        await ArrangeAsync();
        var date = TestData.NextBookableWeekday();
        var time = await BookingFlow.FirstAvailableTimeAsync(Client, Slug, date);

        await BookingFlow.BookAsync(Client, Slug, date, time);

        var slots = await BookingFlow.GetSlotsAsync(Client, Slug, date, date);
        var offered = Enumerable.Range(0, slots.GetArrayLength())
            .Select(i => slots[i].GetProperty("localStartTime").GetString())
            .ToList();

        Assert.DoesNotContain(time.ToString("HH:mm:ss"), offered);
    }

    [Fact]
    public async Task TheBookingIsHandedToTheCalendarSyncSeam()
    {
        // Best-effort and after the commit, but it must actually be attempted -
        // and here it reaches a recorder rather than Google.
        await ArrangeAsync();
        var date = TestData.NextBookableWeekday();
        var time = await BookingFlow.FirstAvailableTimeAsync(Client, Slug, date);

        await BookingFlow.BookAsync(Client, Slug, date, time);

        var session = await WithDbAsync(db => db.BookingSessions.SingleAsync());
        Assert.Contains(session.Id, Factory.Calendar.Created);
    }

    [Fact]
    public async Task SubmittingWithoutADateIsRejected()
    {
        await ArrangeAsync();
        var sessionId = await BookingFlow.StartSessionAsync(Client, Slug);

        await BookingFlow.AppendEventsAsync(sessionId: sessionId, client: Client, events:
        [
            new BookingFlow.ClientEvent("FieldChanged", "Name", "Jane Doe", 1),
            new BookingFlow.ClientEvent("FieldChanged", "Email", "jane@example.com", 2),
        ]);

        var response = await BookingFlow.SubmitAsync(Client, sessionId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await WithDbAsync(db =>
            db.BookingSessions.Where(s => s.Status == BookingSessionStatus.Submitted).ToListAsync()));
    }

    [Fact]
    public async Task TheSessionRebuildsFromItsOwnEventLog()
    {
        // The project's signature claim, asserted across the HTTP boundary: the
        // projection and a pure replay of the log describe the same session.
        await ArrangeAsync();
        var date = TestData.NextBookableWeekday();
        var time = await BookingFlow.FirstAvailableTimeAsync(Client, Slug, date);

        var sessionId = await BookingFlow.StartSessionAsync(Client, Slug);
        await BookingFlow.FillAsync(Client, sessionId, date, time);
        await BookingFlow.SubmitAsync(Client, sessionId);

        var projection = await ReadJsonAsync(await Client.GetAsync($"/api/booking-sessions/{sessionId}"));
        var rebuilt = await ReadJsonAsync(await Client.GetAsync($"/api/booking-sessions/{sessionId}/rebuild"));

        // Every field BookingSessionDto carries, compared as raw JSON so a
        // difference in type or null-ness counts as a difference too.
        foreach (var property in new[]
                 {
                     "status", "name", "email", "phone", "message",
                     "selectedDate", "selectedTime", "meetingProvider", "meetingUrl", "answers",
                 })
        {
            Assert.Equal(
                projection.GetProperty(property).GetRawText(),
                rebuilt.GetProperty(property).GetRawText());
        }

        Assert.Equal("Submitted", rebuilt.GetProperty("status").GetString());
    }

    // ---- Conflict -----------------------------------------------------------

    [Fact]
    public async Task BookingAnAlreadyTakenSlotIsA409()
    {
        // Sequential, not concurrent: the InMemory provider ignores the
        // Serializable transaction the real handler opens, so nothing about
        // race safety may be claimed here. What IS being pinned is that a second
        // booker who is simply too late gets a 409 rather than a duplicate.
        await ArrangeAsync();
        var date = TestData.NextBookableWeekday();
        var time = await BookingFlow.FirstAvailableTimeAsync(Client, Slug, date);

        await BookingFlow.BookAsync(Client, Slug, date, time, "First", "first@example.com");

        var secondSession = await BookingFlow.StartSessionAsync(Client, Slug);
        await BookingFlow.FillAsync(Client, secondSession, date, time, "Second", "second@example.com");
        var response = await BookingFlow.SubmitAsync(Client, secondSession);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ARefusedSecondBookingSaysWhatToDoAndPersistsNothing()
    {
        await ArrangeAsync();
        var date = TestData.NextBookableWeekday();
        var time = await BookingFlow.FirstAvailableTimeAsync(Client, Slug, date);

        await BookingFlow.BookAsync(Client, Slug, date, time, "First", "first@example.com");

        var secondSession = await BookingFlow.StartSessionAsync(Client, Slug);
        await BookingFlow.FillAsync(Client, secondSession, date, time, "Second", "second@example.com");
        var response = await BookingFlow.SubmitAsync(Client, secondSession);

        var body = await ReadJsonAsync(response);
        // The frontend keys its "Choose another time" recovery off the 409 and
        // shows this title verbatim, so it must be a sentence a guest can read.
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("title").GetString()));

        var submitted = await WithDbAsync(db =>
            db.BookingSessions.Where(s => s.Status == BookingSessionStatus.Submitted).ToListAsync());
        Assert.Single(submitted);
        Assert.Equal("First", submitted[0].Name);
    }

    [Fact]
    public async Task ADailyCapRefusesTheBookingBeyondIt()
    {
        await ArrangeAsync(maxBookingsPerDay: 1);
        var date = TestData.NextBookableWeekday();

        var slots = await BookingFlow.GetSlotsAsync(Client, Slug, date, date);
        var firstTime = TimeOnly.Parse(slots[0].GetProperty("localStartTime").GetString()!);
        var secondTime = TimeOnly.Parse(slots[1].GetProperty("localStartTime").GetString()!);

        await BookingFlow.BookAsync(Client, Slug, date, firstTime, "First", "first@example.com");

        // The cap is reached, so the day should now offer nothing at all.
        var remaining = await BookingFlow.GetSlotsAsync(Client, Slug, date, date);
        Assert.Equal(0, remaining.GetArrayLength());

        var secondSession = await BookingFlow.StartSessionAsync(Client, Slug);
        await BookingFlow.FillAsync(Client, secondSession, date, secondTime, "Second", "second@example.com");
        var response = await BookingFlow.SubmitAsync(Client, secondSession);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---- Guest confirmation flag -------------------------------------------

    [Fact]
    public async Task GuestConfirmationIsReportedQueuedWhenNotificationsAreOn()
    {
        var workspace = await ArrangeAsync();
        await WithDbAsync(db => TestData.SetNotificationSettingsAsync(db, workspace.Organizer.Id, notifyGuest: true));

        var date = TestData.NextBookableWeekday();
        var time = await BookingFlow.FirstAvailableTimeAsync(Client, Slug, date);
        var confirmation = await BookingFlow.BookAsync(Client, Slug, date, time);

        Assert.True(confirmation.GetProperty("guestConfirmationQueued").GetBoolean());
    }

    [Fact]
    public async Task GuestConfirmationIsReportedNotQueuedWhenTheOrganizerTurnedItOff()
    {
        // The flag exists because the screens used to promise an email whenever
        // the booking carried an address - a different fact entirely.
        var workspace = await ArrangeAsync();
        await WithDbAsync(db => TestData.SetNotificationSettingsAsync(db, workspace.Organizer.Id, notifyGuest: false));

        var date = TestData.NextBookableWeekday();
        var time = await BookingFlow.FirstAvailableTimeAsync(Client, Slug, date);
        var confirmation = await BookingFlow.BookAsync(Client, Slug, date, time);

        Assert.False(confirmation.GetProperty("guestConfirmationQueued").GetBoolean());
    }
}
