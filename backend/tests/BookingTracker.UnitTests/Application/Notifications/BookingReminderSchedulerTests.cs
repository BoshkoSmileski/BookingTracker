using BookingTracker.Application.Notifications;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.Infrastructure.Persistence;
using BookingTracker.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookingTracker.UnitTests.Application.Notifications;

/// <summary>
/// The scheduler decides which reminders exist for a booking and when, which is
/// where every reminder requirement actually lands (multiple intervals,
/// duplicate prevention, cancellation, reschedule regeneration, settings
/// resync). Run against a real InMemory DbContext, since all of it is
/// cross-table query behaviour.
///
/// Dates are computed from DateTime.UtcNow rather than hardcoded - the
/// scheduler compares every reminder against "now" to decide whether its moment
/// has already passed, so a fixed date would change meaning as the wall clock
/// moves.
/// </summary>
public class BookingReminderSchedulerTests
{
    private static BookingReminderScheduler CreateScheduler(BookingTrackerDbContext db) =>
        new(db, NullLogger<BookingReminderScheduler>.Instance);

    /// <summary>Seeds an organizer + page + submitted booking whose meeting is <paramref name="daysOut"/> days from now, in UTC.</summary>
    private static async Task<(Organizer Organizer, BookingPage Page, BookingSession Session)> SeedAsync(
        BookingTrackerDbContext db, int daysOut = 5, IReadOnlyList<int>? reminderMinutes = null, bool remindersEnabled = true)
    {
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        db.WorkingSchedules.Add(TestEntities.CreateWorkingSchedule(organizer.Id, "UTC"));

        var settings = NotificationSettings.CreateDefault(organizer.Id);
        settings.UpdateSettings(true, true, remindersEnabled, reminderMinutes ?? [1440]);
        db.NotificationSettings.Add(settings);

        var meeting = DateTime.UtcNow.AddDays(daysOut);
        var result = BookingSessionScenarios.StartFillAndSubmit(
            page.Id, year: meeting.Year, month: meeting.Month, day: meeting.Day, hour: meeting.Hour, minute: 0);
        db.BookingSessions.Add(result.Session);
        db.BookingSessionEvents.AddRange(result.Events);
        await db.SaveChangesAsync();

        return (organizer, page, result.Session);
    }

    [Fact]
    public async Task ScheduleForBooking_CreatesOneReminderPerConfiguredInterval()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (_, _, session) = await SeedAsync(db, reminderMinutes: [1440, 60, 15]);

        await CreateScheduler(db).ScheduleForBookingAsync(session);

        var reminders = await db.BookingReminders.ToListAsync();
        Assert.Equal(3, reminders.Count);
        Assert.Equal([15, 60, 1440], reminders.Select(r => r.MinutesBeforeEvent).OrderBy(m => m));
        Assert.All(reminders, r => Assert.Equal(BookingReminderStatus.Scheduled, r.Status));
    }

    [Fact]
    public async Task ScheduleForBooking_IsIdempotent_NoDuplicatesOnRepeatCalls()
    {
        // Requirement: the same reminder must never exist twice, no matter how often
        // scheduling runs (retries, restarts, a resync landing on the same booking).
        await using var db = InMemoryDbContextFactory.Create();
        var (_, _, session) = await SeedAsync(db, reminderMinutes: [1440, 60]);
        var scheduler = CreateScheduler(db);

        await scheduler.ScheduleForBookingAsync(session);
        await scheduler.ScheduleForBookingAsync(session);
        await scheduler.ScheduleForBookingAsync(session);

        Assert.Equal(2, await db.BookingReminders.CountAsync());
    }

    [Fact]
    public async Task ScheduleForBooking_SkipsIntervalsWhoseMomentHasAlreadyPassed()
    {
        // Booked ~2 hours out with a 24-hour reminder configured: that reminder's time
        // is already gone, so no row is created at all rather than one that would
        // immediately be reported as missed.
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        db.WorkingSchedules.Add(TestEntities.CreateWorkingSchedule(organizer.Id, "UTC"));
        var settings = NotificationSettings.CreateDefault(organizer.Id);
        settings.UpdateSettings(true, true, true, [1440, 60]);
        db.NotificationSettings.Add(settings);

        var meeting = DateTime.UtcNow.AddHours(2);
        var result = BookingSessionScenarios.StartFillAndSubmit(
            page.Id, year: meeting.Year, month: meeting.Month, day: meeting.Day, hour: meeting.Hour, minute: meeting.Minute);
        db.BookingSessions.Add(result.Session);
        db.BookingSessionEvents.AddRange(result.Events);
        await db.SaveChangesAsync();

        await CreateScheduler(db).ScheduleForBookingAsync(result.Session);

        var reminder = Assert.Single(await db.BookingReminders.ToListAsync());
        Assert.Equal(60, reminder.MinutesBeforeEvent); // the 1-hour one is still ahead; the 24-hour one is not
    }

    [Fact]
    public async Task ScheduleForBooking_WithRemindersDisabled_CreatesNothing()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (_, _, session) = await SeedAsync(db, remindersEnabled: false);

        await CreateScheduler(db).ScheduleForBookingAsync(session);

        Assert.Empty(await db.BookingReminders.ToListAsync());
    }

    [Fact]
    public async Task ScheduleForBooking_ForANonSubmittedSession_CreatesNothing()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        var (session, startEvent) = BookingSession.Start(page.Id, BookingSessionScenarios.SampleContext);
        db.BookingSessions.Add(session);
        db.BookingSessionEvents.Add(startEvent);
        await db.SaveChangesAsync();

        await CreateScheduler(db).ScheduleForBookingAsync(session);

        Assert.Empty(await db.BookingReminders.ToListAsync());
    }

    [Fact]
    public async Task CancelForBooking_CancelsScheduledReminders_ButLeavesQueuedOnesAsHistory()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (_, _, session) = await SeedAsync(db, reminderMinutes: [1440, 60]);
        var scheduler = CreateScheduler(db);
        await scheduler.ScheduleForBookingAsync(session);

        // Pretend the 24-hour reminder already went out.
        var queued = await db.BookingReminders.FirstAsync(r => r.MinutesBeforeEvent == 1440);
        queued.MarkQueued(Guid.NewGuid());
        await db.SaveChangesAsync();

        await scheduler.CancelForBookingAsync(session, "Booking cancelled");

        var reminders = await db.BookingReminders.ToListAsync();
        Assert.Equal(BookingReminderStatus.Queued, reminders.Single(r => r.MinutesBeforeEvent == 1440).Status);
        var cancelled = reminders.Single(r => r.MinutesBeforeEvent == 60);
        Assert.Equal(BookingReminderStatus.Cancelled, cancelled.Status);
        Assert.Equal("Booking cancelled", cancelled.ResolutionReason);
    }

    [Fact]
    public async Task RescheduleForBooking_CancelsOldReminders_AndGeneratesThemForTheNewTime()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (_, _, session) = await SeedAsync(db, daysOut: 5, reminderMinutes: [1440, 60]);
        var scheduler = CreateScheduler(db);
        await scheduler.ScheduleForBookingAsync(session);

        var originalScheduledFor = (await db.BookingReminders.ToListAsync())
            .ToDictionary(r => r.MinutesBeforeEvent, r => r.ScheduledForUtc);

        var newMeeting = DateTime.UtcNow.AddDays(9);
        session.Reschedule(
            DateOnly.FromDateTime(newMeeting), new TimeOnly(newMeeting.Hour, 0), BookingSessionScenarios.SampleContext);
        await db.SaveChangesAsync();

        await scheduler.RescheduleForBookingAsync(session);

        var reminders = await db.BookingReminders.ToListAsync();
        var live = reminders.Where(r => r.Status == BookingReminderStatus.Scheduled).ToList();
        var cancelled = reminders.Where(r => r.Status == BookingReminderStatus.Cancelled).ToList();

        Assert.Equal(2, live.Count);
        Assert.Equal(2, cancelled.Count);
        Assert.All(cancelled, r => Assert.Equal("Booking rescheduled", r.ResolutionReason));
        // The new ones point at the new meeting time, not the old one.
        Assert.All(live, r => Assert.NotEqual(originalScheduledFor[r.MinutesBeforeEvent], r.ScheduledForUtc));
    }

    [Fact]
    public async Task RescheduleForBooking_DoesNotDuplicateReminders()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (_, _, session) = await SeedAsync(db, daysOut: 5, reminderMinutes: [1440]);
        var scheduler = CreateScheduler(db);
        await scheduler.ScheduleForBookingAsync(session);

        var newMeeting = DateTime.UtcNow.AddDays(9);
        session.Reschedule(DateOnly.FromDateTime(newMeeting), new TimeOnly(newMeeting.Hour, 0), BookingSessionScenarios.SampleContext);
        await db.SaveChangesAsync();

        await scheduler.RescheduleForBookingAsync(session);
        await scheduler.RescheduleForBookingAsync(session);

        // Exactly one live reminder however many times reschedule handling runs.
        Assert.Single(await db.BookingReminders.Where(r => r.Status == BookingReminderStatus.Scheduled).ToListAsync());
    }

    [Fact]
    public async Task ResyncForOrganizer_AddsNewlyEnabledIntervals_AndCancelsRemovedOnes()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (organizer, _, session) = await SeedAsync(db, reminderMinutes: [1440]);
        var scheduler = CreateScheduler(db);
        await scheduler.ScheduleForBookingAsync(session);

        // Organizer swaps 24 hours for 1 hour + 15 minutes.
        var settings = await db.NotificationSettings.FirstAsync(s => s.OrganizerId == organizer.Id);
        settings.UpdateSettings(true, true, true, [60, 15]);
        await db.SaveChangesAsync();

        await scheduler.ResyncForOrganizerAsync(organizer.Id);

        var reminders = await db.BookingReminders.ToListAsync();
        Assert.Equal(BookingReminderStatus.Cancelled, reminders.Single(r => r.MinutesBeforeEvent == 1440).Status);
        var live = reminders.Where(r => r.Status == BookingReminderStatus.Scheduled).Select(r => r.MinutesBeforeEvent).OrderBy(m => m);
        Assert.Equal([15, 60], live);
    }

    [Fact]
    public async Task ResyncForOrganizer_WithRemindersTurnedOff_CancelsEverythingPending()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (organizer, _, session) = await SeedAsync(db, reminderMinutes: [1440, 60]);
        var scheduler = CreateScheduler(db);
        await scheduler.ScheduleForBookingAsync(session);

        var settings = await db.NotificationSettings.FirstAsync(s => s.OrganizerId == organizer.Id);
        settings.UpdateSettings(true, true, remindersEnabled: false, reminderMinutesBeforeEvent: []);
        await db.SaveChangesAsync();

        await scheduler.ResyncForOrganizerAsync(organizer.Id);

        var reminders = await db.BookingReminders.ToListAsync();
        Assert.All(reminders, r => Assert.Equal(BookingReminderStatus.Cancelled, r.Status));
    }
}
