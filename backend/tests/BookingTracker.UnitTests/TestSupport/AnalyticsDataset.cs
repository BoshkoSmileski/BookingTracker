using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.Infrastructure.Persistence;

namespace BookingTracker.UnitTests.TestSupport;

/// <summary>
/// One organizer with a realistic spread of sessions, shared by the analytics
/// query tests and the analytics export tests.
///
/// It lives here rather than being written out in each file so the two describe
/// the same dataset by construction: the export tests assert that a download
/// matches the dashboard, which only means something if both are looking at
/// identical data.
///
/// Meeting dates are derived from DateTime.UtcNow, never written down: the
/// upcoming/completed split reads the clock, so a fixed date would quietly change
/// what the fixture means as time passes.
/// </summary>
public static class AnalyticsDataset
{
    public static readonly DateTime FutureMeeting = DateTime.UtcNow.AddDays(10);
    public static readonly DateTime PastMeeting = DateTime.UtcNow.AddDays(-10);

    public sealed record Seeded(Organizer Organizer, BookingPage PageA, BookingPage PageB);

    /// <summary>
    /// Page A: one upcoming booking, one completed booking, one cancelled, one
    /// abandoned (reached the time step), one still active. Page B: one upcoming
    /// booking. Six sessions in total, three of them confirmed.
    /// </summary>
    public static async Task<Seeded> SeedAsync(BookingTrackerDbContext db)
    {
        var organizer = TestEntities.CreateOrganizer();
        var pageA = TestEntities.CreateBookingPage(organizer.Id, slug: "page-a");
        var pageB = TestEntities.CreateBookingPage(organizer.Id, slug: "page-b");
        db.Organizers.Add(organizer);
        db.BookingPages.AddRange(pageA, pageB);
        db.WorkingSchedules.Add(TestEntities.CreateWorkingSchedule(organizer.Id, "UTC"));

        void AddSubmitted(Guid pageId, DateTime meeting, bool cancel = false)
        {
            var r = BookingSessionScenarios.StartFillAndSubmit(
                pageId, year: meeting.Year, month: meeting.Month, day: meeting.Day, hour: meeting.Hour, minute: 0);
            if (cancel)
            {
                r.Events.Add(r.Session.Cancel(CancelledByType.Customer, "changed my mind", BookingSessionScenarios.SampleContext));
            }
            db.BookingSessions.Add(r.Session);
            db.BookingSessionEvents.AddRange(r.Events);
        }

        AddSubmitted(pageA.Id, FutureMeeting);
        AddSubmitted(pageA.Id, PastMeeting);
        AddSubmitted(pageA.Id, FutureMeeting.AddHours(1), cancel: true);
        AddSubmitted(pageB.Id, FutureMeeting.AddHours(2));

        // Abandoned: got as far as picking a time, never entered details.
        var (abandoned, abandonedStart) = BookingSession.Start(pageA.Id, BookingSessionScenarios.SampleContext);
        var dateEvent = abandoned.SelectDate(DateOnly.FromDateTime(FutureMeeting), 1, BookingSessionScenarios.SampleContext);
        var timeEvent = abandoned.SelectTime(new TimeOnly(11, 0), 2, BookingSessionScenarios.SampleContext);
        var abandonEvent = abandoned.Abandon();
        db.BookingSessions.Add(abandoned);
        // Abandon() returns null for a session that was already terminal; this one
        // is active, so the event is real - asserted rather than assumed, because a
        // null slipping into the log would silently weaken every funnel assertion.
        Assert.NotNull(abandonEvent);
        db.BookingSessionEvents.AddRange([abandonedStart, dateEvent, timeEvent, abandonEvent]);

        // Active: just landed, nothing chosen.
        var (active, activeStart) = BookingSession.Start(pageA.Id, BookingSessionScenarios.SampleContext);
        db.BookingSessions.Add(active);
        db.BookingSessionEvents.Add(activeStart);

        await db.SaveChangesAsync();
        return new Seeded(organizer, pageA, pageB);
    }
}
