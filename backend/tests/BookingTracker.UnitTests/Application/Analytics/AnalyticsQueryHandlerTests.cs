using BookingTracker.Application.Analytics;
using BookingTracker.Application.Analytics.Queries.GetActivityFeed;
using BookingTracker.Application.Analytics.Queries.GetBookingAnalytics;
using BookingTracker.Application.Analytics.Queries.GetConversionFunnel;
using BookingTracker.Application.Analytics.Queries.GetOperationalAnalytics;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.Infrastructure.Persistence;
using BookingTracker.UnitTests.TestSupport;

namespace BookingTracker.UnitTests.Application.Analytics;

/// <summary>
/// Analytics are pure read-side aggregation over data other features already
/// write, so these run against a real InMemory DbContext with realistic seeded
/// sessions - a mocked DbSet would not exercise the grouping that is the whole
/// point of these handlers.
///
/// Dates are computed relative to DateTime.UtcNow: the upcoming/completed split
/// and the trend's gap-filling both read the clock, so fixed dates would change
/// meaning as time passes.
/// </summary>
public class AnalyticsQueryHandlerTests
{
    private static GetBookingAnalyticsQueryHandler BookingHandler(BookingTrackerDbContext db) => new(db);

    [Fact]
    public async Task BookingAnalytics_SummarisesEveryStatus()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var seeded = await AnalyticsDataset.SeedAsync(db);

        var result = await BookingHandler(db).Handle(
            new GetBookingAnalyticsQuery(new AnalyticsFilter(seeded.Organizer.Id)), CancellationToken.None);

        Assert.Equal(6, result.Summary.TotalSessions);
        Assert.Equal(3, result.Summary.Confirmed);   // 2 page A (one past, one future) + 1 page B
        Assert.Equal(1, result.Summary.Cancelled);
        Assert.Equal(1, result.Summary.Abandoned);
        Assert.Equal(1, result.Summary.InProgress);
        Assert.Equal(1, result.Summary.Completed);   // the meeting in the past
        Assert.Equal(2, result.Summary.Upcoming);
        Assert.Equal(2, result.Summary.TotalBookingPages);
        Assert.Equal(0.5, result.Summary.CompletionRate, 3); // 3 of 6
    }

    [Fact]
    public async Task BookingAnalytics_StatusDistribution_IsMutuallyExclusive_AndSharesSumToOne()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var seeded = await AnalyticsDataset.SeedAsync(db);

        var result = await BookingHandler(db).Handle(
            new GetBookingAnalyticsQuery(new AnalyticsFilter(seeded.Organizer.Id)), CancellationToken.None);

        // "Rescheduled" must never be a slice - it overlaps every other category.
        Assert.DoesNotContain(result.StatusDistribution, s => s.Status == "Rescheduled");
        Assert.Equal(result.Summary.TotalSessions, result.StatusDistribution.Sum(s => s.Count));
        Assert.Equal(1.0, result.StatusDistribution.Sum(s => s.Share), 3);
    }

    [Fact]
    public async Task BookingAnalytics_FiltersByBookingPage()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var seeded = await AnalyticsDataset.SeedAsync(db);

        var result = await BookingHandler(db).Handle(
            new GetBookingAnalyticsQuery(new AnalyticsFilter(seeded.Organizer.Id, BookingPageId: seeded.PageB.Id)), CancellationToken.None);

        Assert.Equal(1, result.Summary.TotalSessions);
        Assert.Equal(1, result.Summary.Confirmed);
        Assert.Equal(seeded.PageB.Id, Assert.Single(result.PagePerformance).BookingPageId);
    }

    [Fact]
    public async Task BookingAnalytics_ForAnotherOrganizersPage_ThrowsNotFound()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var seeded = await AnalyticsDataset.SeedAsync(db);
        var stranger = TestEntities.CreateOrganizer(email: "stranger@example.com");
        db.Organizers.Add(stranger);
        await db.SaveChangesAsync();

        // Ownership: asking for someone else's page is a 404, never a silent empty dashboard.
        await Assert.ThrowsAsync<NotFoundException>(() => BookingHandler(db).Handle(
            new GetBookingAnalyticsQuery(new AnalyticsFilter(stranger.Id, BookingPageId: seeded.PageA.Id)), CancellationToken.None));
    }

    [Fact]
    public async Task BookingAnalytics_PagePerformance_ComputesConversionAndCancellationRates()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var seeded = await AnalyticsDataset.SeedAsync(db);

        var result = await BookingHandler(db).Handle(
            new GetBookingAnalyticsQuery(new AnalyticsFilter(seeded.Organizer.Id)), CancellationToken.None);

        var pageA = result.PagePerformance.Single(p => p.BookingPageId == seeded.PageA.Id);
        Assert.Equal(5, pageA.Views);
        Assert.Equal(2, pageA.Bookings);
        Assert.Equal(1, pageA.Cancelled);
        Assert.Equal(0.4, pageA.ConversionRate, 3);            // 2 bookings of 5 sessions
        Assert.Equal(1.0 / 3, pageA.CancellationRate, 3);      // 1 cancelled of 3 that became bookings
    }

    [Fact]
    public async Task BookingAnalytics_WeekdayAndHour_UseTheBookingsOwnLocalSlot()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var seeded = await AnalyticsDataset.SeedAsync(db);

        var result = await BookingHandler(db).Handle(
            new GetBookingAnalyticsQuery(new AnalyticsFilter(seeded.Organizer.Id)), CancellationToken.None);

        Assert.Equal(7, result.BookingsByWeekday.Count); // always all seven, zero-filled
        Assert.Equal(3, result.BookingsByWeekday.Sum(d => d.Count));
        Assert.Equal(3, result.BookingsByHour.Sum(h => h.Count));
    }

    [Fact]
    public async Task BookingAnalytics_CompletionTime_ReportsAverageMedianAndExtremes()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var seeded = await AnalyticsDataset.SeedAsync(db);

        var result = await BookingHandler(db).Handle(
            new GetBookingAnalyticsQuery(new AnalyticsFilter(seeded.Organizer.Id)), CancellationToken.None);

        // 4, not 3: the later-cancelled booking was still *booked*, so its
        // start-to-confirmation time is a real data point. "Time to book" measures the
        // visitor's journey, which a subsequent cancellation does not retroactively undo.
        Assert.Equal(4, result.CompletionTime.SampleSize);
        Assert.NotNull(result.CompletionTime.AverageSeconds);
        Assert.NotNull(result.CompletionTime.MedianSeconds);
        Assert.True(result.CompletionTime.FastestSeconds <= result.CompletionTime.SlowestSeconds);
    }

    [Fact]
    public async Task BookingAnalytics_WithNoData_ReturnsZeroesRatherThanThrowing()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = TestEntities.CreateOrganizer();
        db.Organizers.Add(organizer);
        await db.SaveChangesAsync();

        var result = await BookingHandler(db).Handle(
            new GetBookingAnalyticsQuery(new AnalyticsFilter(organizer.Id)), CancellationToken.None);

        Assert.Equal(0, result.Summary.TotalSessions);
        Assert.Equal(0, result.Summary.CompletionRate);
        Assert.Empty(result.StatusDistribution);
        Assert.Equal(0, result.CompletionTime.SampleSize);
    }

    [Fact]
    public async Task ConversionFunnel_CountsDistinctSessionsPerStep_FromTheEventLog()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var seeded = await AnalyticsDataset.SeedAsync(db);

        var result = await new GetConversionFunnelQueryHandler(db).Handle(
            new GetConversionFunnelQuery(new AnalyticsFilter(seeded.Organizer.Id)), CancellationToken.None);

        var byStep = result.Steps.ToDictionary(s => s.Step, s => s.Count);
        Assert.Equal(6, byStep["Page viewed"]);        // every session started
        Assert.Equal(5, byStep["Date selected"]);      // 4 submitted + the abandoned one
        Assert.Equal(5, byStep["Time selected"]);
        Assert.Equal(4, byStep["Details entered"]);    // abandoned session never typed details
        Assert.Equal(4, byStep["Booking confirmed"]);  // includes the one later cancelled
        Assert.Equal(4.0 / 6, result.ConversionRate, 3);
    }

    [Fact]
    public async Task ConversionFunnel_StepConversion_IsRelativeToThePreviousStep()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var seeded = await AnalyticsDataset.SeedAsync(db);

        var result = await new GetConversionFunnelQueryHandler(db).Handle(
            new GetConversionFunnelQuery(new AnalyticsFilter(seeded.Organizer.Id)), CancellationToken.None);

        var details = result.Steps.Single(s => s.Step == "Details entered");
        Assert.Equal(4.0 / 5, details.StepConversion, 3);  // of the 5 that picked a time
        Assert.Equal(4.0 / 6, details.ShareOfEntry, 3);    // of the 6 that arrived
    }

    [Fact]
    public async Task Abandonment_ReportsRateAndFurthestStepReached()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var seeded = await AnalyticsDataset.SeedAsync(db);

        var result = await new GetConversionFunnelQueryHandler(db).Handle(
            new GetConversionFunnelQuery(new AnalyticsFilter(seeded.Organizer.Id)), CancellationToken.None);

        Assert.Equal(1, result.Abandonment.TotalAbandoned);
        Assert.Equal(1.0 / 6, result.Abandonment.AbandonmentRate, 3);
        // The abandoned session picked a time but never entered details -> step 3.
        Assert.Equal(3, result.Abandonment.AverageStepReached);
        Assert.Equal("Time selected", result.Abandonment.MostCommonStep);
    }

    [Fact]
    public async Task OperationalAnalytics_AggregatesEmailByStatusAndType()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var seeded = await AnalyticsDataset.SeedAsync(db);
        var sessionId = db.BookingSessions.First(s => s.Status == BookingSessionStatus.Submitted).Id;

        var sent = EmailNotification.Create(sessionId, EmailNotificationType.BookingConfirmation, "a@example.com", "A", "s", "h", "t", "BookingConfirmation", 3);
        sent.MarkSent();
        var failed = EmailNotification.Create(sessionId, EmailNotificationType.Reminder, "b@example.com", "B", "s", "h", "t", "24h", 1);
        failed.RecordFailedAttempt("smtp down", TimeSpan.FromMinutes(1));
        var pending = EmailNotification.Create(sessionId, EmailNotificationType.Reminder, "c@example.com", "C", "s", "h", "t", "1h", 3);
        db.EmailNotifications.AddRange(sent, failed, pending);
        await db.SaveChangesAsync();

        var result = await new GetOperationalAnalyticsQueryHandler(db).Handle(
            new GetOperationalAnalyticsQuery(new AnalyticsFilter(seeded.Organizer.Id)), CancellationToken.None);

        Assert.Equal(3, result.Email.Total);
        Assert.Equal(1, result.Email.Sent);
        Assert.Equal(1, result.Email.Failed);
        Assert.Equal(1, result.Email.Pending);
        Assert.Equal(1, result.Email.Retries);
        // Rate is over resolved notifications only - a pending queue is not a failure.
        Assert.Equal(0.5, result.Email.DeliveryRate, 3);
        Assert.Equal(2, result.Email.ByType.Single(t => t.NotificationType == nameof(EmailNotificationType.Reminder)).Total);
    }

    [Fact]
    public async Task OperationalAnalytics_ResolvesReminderOutcomesLikeThePerBookingView()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var seeded = await AnalyticsDataset.SeedAsync(db);
        var session = db.BookingSessions.First(s => s.Status == BookingSessionStatus.Submitted);

        var deliveredNotification = EmailNotification.Create(
            session.Id, EmailNotificationType.Reminder, "a@example.com", "A", "s", "h", "t", "24h", 3);
        deliveredNotification.MarkSent();
        db.EmailNotifications.Add(deliveredNotification);

        var deliveredReminder = BookingReminder.Schedule(session.Id, session.BookingPageId, 1440, AnalyticsDataset.FutureMeeting);
        deliveredReminder.MarkQueued(deliveredNotification.Id);
        var upcoming = BookingReminder.Schedule(session.Id, session.BookingPageId, 60, AnalyticsDataset.FutureMeeting);
        var cancelled = BookingReminder.Schedule(session.Id, session.BookingPageId, 30, AnalyticsDataset.FutureMeeting);
        cancelled.Cancel("Booking cancelled");
        db.BookingReminders.AddRange(deliveredReminder, upcoming, cancelled);
        await db.SaveChangesAsync();

        var result = await new GetOperationalAnalyticsQueryHandler(db).Handle(
            new GetOperationalAnalyticsQuery(new AnalyticsFilter(seeded.Organizer.Id)), CancellationToken.None);

        Assert.Equal(3, result.Reminders.Total);
        Assert.Equal(1, result.Reminders.Sent);       // Queued + notification Sent
        Assert.Equal(1, result.Reminders.Scheduled);
        Assert.Equal(1, result.Reminders.Cancelled);
        Assert.Equal((1440 + 60 + 30) / 3.0, result.Reminders.AverageLeadTimeMinutes!.Value, 3);
    }

    [Fact]
    public async Task OperationalAnalytics_WithNoCalendarConnection_ReportsDisconnectedWithoutCoverage()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var seeded = await AnalyticsDataset.SeedAsync(db);

        var result = await new GetOperationalAnalyticsQueryHandler(db).Handle(
            new GetOperationalAnalyticsQuery(new AnalyticsFilter(seeded.Organizer.Id)), CancellationToken.None);

        Assert.False(result.Calendar.Connected);
        Assert.Null(result.Calendar.SyncCoverage);
        Assert.Equal(0, result.Calendar.SyncedBookings);
        Assert.Equal(3, result.Calendar.ConfirmedBookings);
    }

    [Fact]
    public async Task ActivityFeed_MergesSourcesNewestFirst_AndRespectsTheLimit()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var seeded = await AnalyticsDataset.SeedAsync(db);

        var result = await new GetActivityFeedQueryHandler(db).Handle(
            new GetActivityFeedQuery(new AnalyticsFilter(seeded.Organizer.Id), Limit: 5), CancellationToken.None);

        Assert.True(result.Count <= 5);
        Assert.Equal(result.OrderByDescending(e => e.OccurredAtUtc).Select(e => e.OccurredAtUtc), result.Select(e => e.OccurredAtUtc));
        Assert.Contains(result, e => e.Kind is "BookingConfirmed" or "BookingCancelled" or "BookingPageCreated");
    }

    [Fact]
    public async Task DateRangeFilter_ExcludesSessionsOutsideTheWindow()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var seeded = await AnalyticsDataset.SeedAsync(db);

        // Everything was seeded "now", so a window entirely in the past must be empty.
        var pastOnly = new AnalyticsFilter(
            seeded.Organizer.Id,
            From: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-60)),
            To: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30)));

        var result = await BookingHandler(db).Handle(new GetBookingAnalyticsQuery(pastOnly), CancellationToken.None);

        Assert.Equal(0, result.Summary.TotalSessions);
        // The trend must still span the requested window, gap-filled with zeroes.
        Assert.Equal(31, result.Trend.Count);
        Assert.All(result.Trend, p => Assert.Equal(0, p.Sessions));
    }
}
