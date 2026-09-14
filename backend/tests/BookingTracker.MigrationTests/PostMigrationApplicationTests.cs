using System.Net;
using System.Text.Json;
using BookingTracker.IntegrationTests.Infrastructure;
using BookingTracker.MigrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.MigrationTests;

/// <summary>
/// A migrated schema that matches the model on paper and an application that can
/// actually use the migrated rows are two different claims. This class is the
/// second one.
///
/// Everything here runs the REAL API in-process against the database
/// <see cref="LegacyDatabaseFixture"/> upgraded - real Program.cs, real
/// controllers, real MediatR pipeline, real EF queries - so a column EF cannot
/// map, an index that did not survive, or a legacy row the domain refuses to
/// materialise all surface as a failing request rather than as a passing
/// metadata check.
///
/// **It is deliberately not an end-to-end suite.** It does not re-test the
/// booking wizard, availability rules or conflict semantics; those belong to the
/// unit, HTTP and SQL Server suites, which are faster and far more precise. What
/// is asserted here is only ever "this still works ON MIGRATED DATA": the reads
/// go through legacy rows, and the one write is made against them.
/// </summary>
public class PostMigrationApplicationTests(LegacyDatabaseFixture fixture) : MigrationApiTestBase
{
    /// <summary>
    /// Every table, through the real context the application resolves from DI.
    ///
    /// This is the check that catches a migration which brought the database to
    /// a shape EF cannot read - a column left with the wrong type, a table the
    /// model expects and the migration did not create. Counting rows is enough:
    /// the query has to be translated and executed for the count to come back.
    /// </summary>
    [Fact]
    public async Task EveryTableIsReadableThroughTheApplicationsOwnDbContext()
    {
        await WithDbAsync(async db =>
        {
            Assert.Equal(3, await db.Organizers.CountAsync());
            Assert.Equal(3, await db.BookingPages.CountAsync());
            Assert.Equal(6, await db.BookingSessions.CountAsync());
            Assert.Equal(8, await db.BookingSessionEvents.CountAsync());
            Assert.Equal(2, await db.WorkingSchedules.CountAsync());
            Assert.Equal(14, await db.WorkingDays.CountAsync());
            Assert.Equal(5, await db.AvailabilityExceptions.CountAsync());
            Assert.Equal(2, await db.RefreshTokens.CountAsync());
            Assert.Equal(1, await db.CalendarConnections.CountAsync());
            Assert.Equal(1, await db.CalendarSyncedEvents.CountAsync());

            // Introduced by the tested range, and empty in a legacy database.
            Assert.Equal(0, await db.AvailabilityOverrides.CountAsync());
            Assert.Equal(0, await db.NotificationSettings.CountAsync());
            Assert.Equal(0, await db.EmailNotifications.CountAsync());
            Assert.Equal(0, await db.BookingReminders.CountAsync());

            // The owned collections, which are separate tables reached through
            // their owner rather than through a DbSet of their own.
            var wednesday = await db.WorkingDays.AsNoTracking()
                .SingleAsync(d => d.WorkingScheduleId == LegacyData.AdaScheduleId && d.DayOfWeek == DayOfWeek.Wednesday);
            Assert.Equal(2, wednesday.Intervals.Count);
            Assert.Equal(new TimeOnly(9, 0), wednesday.Intervals[0].Start);
            Assert.Equal(new TimeOnly(13, 0), wednesday.Intervals[1].Start);
        });
    }

    /// <summary>
    /// The legacy booking page, read the way a guest reads it - which also
    /// proves the legacy BookingQuestions rows still reach the public DTO and
    /// that the organizer's timezone still resolves.
    /// </summary>
    [Fact]
    public async Task ALegacyBookingPageIsStillServedToGuests()
    {
        var response = await Client.GetAsync($"/api/booking-pages/{LegacyData.AdaConsultationSlug}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ReadJsonAsync(response);
        Assert.Equal("Legacy consultation", page.GetProperty("title").GetString());
        Assert.Equal(30, page.GetProperty("durationMinutes").GetInt32());
        Assert.Equal(LegacyData.AdaTimeZoneId, page.GetProperty("timeZoneId").GetString());

        // MeetingProvider is the column AddBookingMeetingProvider added with a
        // default of 0, and the API carries every enum as a string.
        Assert.Equal("None", page.GetProperty("meetingProvider").GetString());

        var instructions = page.GetProperty("instructions");
        Assert.Equal(2, instructions.GetArrayLength());
        Assert.Equal("Please have your account number ready.", instructions[0].GetProperty("text").GetString());

        // Introduced by the range, and correctly empty for a legacy page.
        Assert.Equal(0, page.GetProperty("formFields").GetArrayLength());
    }

    /// <summary>
    /// The behavioural counterpart to the backfill assertion.
    ///
    /// <c>AvailabilityException.Covers</c> being right is what makes a blocked
    /// date actually unbookable, and this is the endpoint a guest would hit. Had
    /// the backfill not run, these rows would hold EndDate = year 1, Covers
    /// would return false for every date, and the blocked day below would be
    /// offered for booking - a silent regression with no error anywhere.
    /// </summary>
    [Fact]
    public async Task LegacyBlockedDatesStillRemoveAvailability()
    {
        // The control first: an ordinary weekday with no exception on it still
        // offers slots, so an empty result below means "blocked" rather than
        // "the schedule stopped working".
        var control = await BookingFlow.GetSlotsAsync(
            Client, LegacyData.AdaConsultationSlug, fixture.Dates.UnblockedControl, fixture.Dates.UnblockedControl);
        Assert.True(control.GetArrayLength() > 0,
            $"Expected the unblocked control day {fixture.Dates.UnblockedControl:yyyy-MM-dd} to offer slots.");

        // The whole-day exception: nothing at all.
        var blocked = await BookingFlow.GetSlotsAsync(
            Client, LegacyData.AdaConsultationSlug, fixture.Dates.WholeDayBlocked, fixture.Dates.WholeDayBlocked);
        Assert.Equal(0, blocked.GetArrayLength());

        // The timed exception: the day is open, but 10:00-12:00 is gone. A slot
        // ending exactly at 10:00 does not overlap and is correctly still there.
        var partial = await BookingFlow.GetSlotsAsync(
            Client, LegacyData.AdaConsultationSlug, fixture.Dates.TimedBlocked, fixture.Dates.TimedBlocked);
        var times = partial.EnumerateArray()
            .Select(slot => TimeOnly.Parse(slot.GetProperty("localStartTime").GetString()!))
            .ToList();

        Assert.NotEmpty(times);
        Assert.DoesNotContain(times, t => t >= new TimeOnly(10, 0) && t < new TimeOnly(12, 0));
        Assert.Contains(times, t => t < new TimeOnly(10, 0));
        Assert.Contains(times, t => t >= new TimeOnly(12, 0));
    }

    /// <summary>
    /// The legacy confirmed booking still occupies its slot, through the rebuilt
    /// conflict index.
    ///
    /// <c>NarrowBookingConflictLockFootprint</c> dropped
    /// IX_BookingSessions_BookingPageId_Status and created the covering index in
    /// its place, over rows that were already there. If the rebuild had lost
    /// them, this slot would simply be free again and a guest could double-book
    /// a booking made under the old version.
    /// </summary>
    [Fact]
    public async Task ALegacyConfirmedBookingStillOccupiesItsSlot()
    {
        var takenSlots = await BookingFlow.GetSlotsAsync(
            Client, LegacyData.AdaConsultationSlug, fixture.Dates.BookedDate, fixture.Dates.BookedDate);

        var offered = takenSlots.EnumerateArray()
            .Select(slot => TimeOnly.Parse(slot.GetProperty("localStartTime").GetString()!))
            .ToList();

        Assert.NotEmpty(offered);
        Assert.DoesNotContain(new TimeOnly(10, 0), offered);

        // And the conflict check itself refuses it, not just the slot list.
        var sessionId = await BookingFlow.StartSessionAsync(Client, LegacyData.AdaConsultationSlug);
        await BookingFlow.FillAsync(Client, sessionId, fixture.Dates.BookedDate, new TimeOnly(10, 0),
            "Double Booker", "double@example.invalid");

        var response = await BookingFlow.SubmitAsync(Client, sessionId);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>
    /// The representative write: a complete guest booking on a legacy page,
    /// entirely over HTTP, followed by a check that nothing the migration
    /// preserved has been disturbed by it.
    ///
    /// It reaches four of the tables the tested range created or altered - the
    /// booking lands in BookingSessions (narrowed Message column, rebuilt
    /// conflict index, new nullable meeting columns), its events in
    /// BookingSessionEvents, its confirmation in EmailNotifications, and its
    /// reminder in BookingReminders - so "the migrated database is writable" is
    /// asserted where a booking actually touches it rather than with a
    /// synthetic INSERT.
    /// </summary>
    [Fact]
    public async Task AGuestCanCompleteABookingOnTheMigratedDatabase()
    {
        var date = fixture.Dates.PostMigrationBooking;
        var time = await BookingFlow.FirstAvailableTimeAsync(Client, LegacyData.AdaConsultationSlug, date);

        var confirmation = await BookingFlow.BookAsync(
            Client, LegacyData.AdaConsultationSlug, date, time, "New Guest", "new.guest@example.invalid");

        var reference = confirmation.GetProperty("bookingReference").GetString();
        Assert.False(string.IsNullOrWhiteSpace(reference));

        await WithDbAsync(async db =>
        {
            var booking = await db.BookingSessions.AsNoTracking()
                .SingleAsync(s => s.BookingReference == reference);

            Assert.Equal(date, booking.SelectedDate);
            Assert.Equal(time, booking.SelectedTime);
            Assert.Equal(LegacyData.AdaConsultationPageId, booking.BookingPageId);

            // Rows in tables the migration created, written by the real handlers.
            Assert.True(await db.EmailNotifications.AnyAsync(n => n.BookingSessionId == booking.Id),
                "The confirmation email was not queued, so EmailNotifications is not usable after the upgrade.");
            Assert.True(await db.BookingReminders.AnyAsync(r => r.BookingSessionId == booking.Id),
                "No reminder was scheduled, so BookingReminders is not usable after the upgrade.");

            // And the legacy rows are exactly as the migration left them: the
            // write added, it did not disturb.
            Assert.Equal(3, await db.Organizers.CountAsync());
            Assert.Equal(5, await db.AvailabilityExceptions.CountAsync());

            var legacyBooking = await db.BookingSessions.AsNoTracking()
                .SingleAsync(s => s.Id == LegacyData.SubmittedSessionId);
            Assert.Equal(fixture.Snapshot.Sessions[LegacyData.SubmittedSessionId].Message, legacyBooking.Message);
            Assert.Equal(LegacyData.SubmittedBookingReference, legacyBooking.BookingReference);

            foreach (var (id, before) in fixture.Snapshot.Exceptions)
            {
                var after = await db.AvailabilityExceptions.AsNoTracking().SingleAsync(e => e.Id == id);
                Assert.Equal(before.Date, after.Date);
                Assert.Equal(before.Date, after.EndDate);
            }
        });
    }

    /// <summary>
    /// A write into <c>AvailabilityOverrides</c> - a table that did not exist in
    /// the legacy schema at all - made for a legacy organizer, and read back
    /// through the public slots endpoint.
    ///
    /// This is the one case where a new table has to cooperate with old rows: an
    /// override replaces the weekly hours for its date, but the legacy blocked
    /// date is still subtracted afterwards. Both halves are checked, because
    /// getting only the first right would silently un-block a date the organizer
    /// blocked under the old version.
    /// </summary>
    [Fact]
    public async Task AnOverrideCanBeSavedForALegacyOrganizerAndStillLosesToALegacyBlockedDate()
    {
        var organizer = await WithDbAsync(db => db.Organizers.AsNoTracking().SingleAsync(o => o.Id == LegacyData.AdaId));
        using var client = ClientFor(organizer);

        // A Saturday the weekly schedule leaves closed - so any slot on it can
        // only have come from the override.
        var saturday = fixture.Dates.UnblockedControl;
        while (saturday.DayOfWeek != DayOfWeek.Saturday) saturday = saturday.AddDays(1);

        var saved = await client.PutAsync(
            "/api/organizer/availability/overrides",
            JsonContent(new
            {
                date = saturday.ToString("yyyy-MM-dd"),
                note = "Open this Saturday",
                ranges = new[] { new { start = "09:00:00", end = "12:00:00" } },
            }));
        Assert.True(saved.IsSuccessStatusCode, $"Saving the override failed: {saved.StatusCode} {await saved.Content.ReadAsStringAsync()}");

        var saturdaySlots = await BookingFlow.GetSlotsAsync(Client, LegacyData.AdaConsultationSlug, saturday, saturday);
        Assert.True(saturdaySlots.GetArrayLength() > 0, "The override did not open the Saturday it was saved for.");

        // The same override on the legacy whole-day blocked date must NOT open
        // it: the block is subtracted from override hours exactly as from weekly
        // hours.
        var onBlockedDate = await client.PutAsync(
            "/api/organizer/availability/overrides",
            JsonContent(new
            {
                date = fixture.Dates.WholeDayBlocked.ToString("yyyy-MM-dd"),
                note = "Should have no effect",
                ranges = new[] { new { start = "09:00:00", end = "12:00:00" } },
            }));
        Assert.True(onBlockedDate.IsSuccessStatusCode);

        var blocked = await BookingFlow.GetSlotsAsync(
            Client, LegacyData.AdaConsultationSlug, fixture.Dates.WholeDayBlocked, fixture.Dates.WholeDayBlocked);
        Assert.Equal(0, blocked.GetArrayLength());
    }

    private static StringContent JsonContent(object body)
        => RawJson(JsonSerializer.Serialize(body, Json));
}
