using BookingTracker.Application.Analytics.Dtos;

namespace BookingTracker.UnitTests.TestSupport;

/// <summary>
/// Builds <see cref="AnalyticsReportDto"/> values for the export-writer tests.
///
/// The writers are pure functions of a report, so their tests hand them one
/// directly rather than seeding a database and running four queries first -
/// which keeps a formatting assertion about formatting. The handler tests
/// (AnalyticsExportQueryHandlerTests) cover the other half: that the report
/// reaching these writers really is the dashboard's numbers.
/// </summary>
public static class AnalyticsReports
{
    public static readonly DateTime GeneratedAt = new(2026, 8, 5, 9, 30, 0, DateTimeKind.Utc);

    /// <summary>A report with something in every section, so a test can assert on any of them.</summary>
    public static AnalyticsReportDto Populated(
        AnalyticsReportMetaDto? meta = null,
        IReadOnlyList<TrendPointDto>? trend = null,
        IReadOnlyList<BookingPagePerformanceDto>? pages = null,
        IReadOnlyList<ActivityEntryDto>? activity = null) =>
        new(
            meta ?? Meta(),
            new BookingAnalyticsDto(
                new BookingSummaryDto(
                    TotalSessions: 20, Confirmed: 8, Cancelled: 3, Abandoned: 6, InProgress: 3,
                    Upcoming: 5, Completed: 3, Rescheduled: 2,
                    TotalBookingPages: 2, ActiveBookingPages: 1, CompletionRate: 0.4),
                trend ?? [
                    new TrendPointDto(new DateOnly(2026, 8, 1), 4, 2),
                    new TrendPointDto(new DateOnly(2026, 8, 2), 0, 0),
                    new TrendPointDto(new DateOnly(2026, 8, 3), 6, 3),
                    new TrendPointDto(new DateOnly(2026, 8, 4), 10, 3),
                ],
                [
                    new StatusSliceDto("Upcoming", 5, 0.25),
                    new StatusSliceDto("Completed", 3, 0.15),
                    new StatusSliceDto("Cancelled", 3, 0.15),
                    new StatusSliceDto("Abandoned", 6, 0.30),
                    new StatusSliceDto("In progress", 3, 0.15),
                ],
                [
                    new WeekdayCountDto(0, "Sunday", 0),
                    new WeekdayCountDto(1, "Monday", 4),
                    new WeekdayCountDto(2, "Tuesday", 2),
                    new WeekdayCountDto(3, "Wednesday", 0),
                    new WeekdayCountDto(4, "Thursday", 1),
                    new WeekdayCountDto(5, "Friday", 1),
                    new WeekdayCountDto(6, "Saturday", 0),
                ],
                [new HourCountDto(9, 3), new HourCountDto(10, 4), new HourCountDto(11, 1)],
                new CompletionTimeDto(125, 90, 30, 400, 8),
                pages ?? [
                    new BookingPagePerformanceDto(
                        Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        "30 Minute Meeting", "demo-30-min-meeting", true,
                        Views: 14, Bookings: 6, Cancelled: 2, Upcoming: 4,
                        ConversionRate: 0.4286, CancellationRate: 0.25),
                ],
                "Europe/Skopje"),
            new ConversionFunnelDto(
                [
                    new FunnelStepDto("Page viewed", 20, 1, 1),
                    new FunnelStepDto("Date selected", 14, 0.7, 0.7),
                    new FunnelStepDto("Time selected", 12, 0.6, 0.857),
                    new FunnelStepDto("Details entered", 10, 0.5, 0.833),
                    new FunnelStepDto("Booking confirmed", 8, 0.4, 0.8),
                ],
                0.4,
                new AbandonmentDto(6, 0.3, 2.5, "Date selected",
                [
                    new AbandonmentStepDto("Date selected", 4, 0.667),
                    new AbandonmentStepDto("Page viewed", 2, 0.333),
                ])),
            new OperationalAnalyticsDto(
                new EmailAnalyticsDto(30, 1, 28, 1, 2, 0.9655,
                [
                    new EmailTypeCountDto("BookingConfirmation", 12, 12, 0),
                    new EmailTypeCountDto("Reminder", 6, 5, 1),
                ]),
                new ReminderAnalyticsDto(10, 4, 4, 0, 1, 1, 970),
                new CalendarAnalyticsDto(
                    true, "organizer@gmail.com", "Work", "Connected", "Connected",
                    SyncedBookings: 6, ConfirmedBookings: 8, SyncCoverage: 0.75,
                    LastSuccessfulSyncAtUtc: new DateTime(2026, 8, 4, 18, 15, 0, DateTimeKind.Utc),
                    LastFailedSyncAtUtc: null, LastSyncError: null)),
            activity ?? [
                new ActivityEntryDto("BookingConfirmed", "Booking confirmed", new DateTime(2026, 8, 4, 10, 5, 0, DateTimeKind.Utc), null, null),
                new ActivityEntryDto("EmailFailed", "Reminder email to jane@example.com failed", new DateTime(2026, 8, 4, 9, 0, 0, DateTimeKind.Utc), null, null),
            ]);

    /// <summary>An organizer who has done nothing yet - every count zero, every list empty.</summary>
    public static AnalyticsReportDto Empty(AnalyticsReportMetaDto? meta = null) =>
        new(
            meta ?? Meta(),
            new BookingAnalyticsDto(
                new BookingSummaryDto(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                [], [], [], [],
                new CompletionTimeDto(null, null, null, null, 0),
                [],
                "UTC"),
            new ConversionFunnelDto([], 0, new AbandonmentDto(0, 0, null, null, [])),
            new OperationalAnalyticsDto(
                new EmailAnalyticsDto(0, 0, 0, 0, 0, 0, []),
                new ReminderAnalyticsDto(0, 0, 0, 0, 0, 0, null),
                new CalendarAnalyticsDto(false, null, null, null, null, 0, 0, null, null, null, null)),
            []);

    public static AnalyticsReportMetaDto Meta(
        string organizerName = "Test Organizer",
        string bookingPageLabel = "All booking pages",
        string statusLabel = "All statuses",
        DateOnly? from = null,
        DateOnly? to = null) =>
        new(
            organizerName,
            "organizer@example.com",
            GeneratedAt,
            from ?? new DateOnly(2026, 7, 6),
            to ?? new DateOnly(2026, 8, 4),
            "2026-07-06 to 2026-08-04",
            bookingPageLabel,
            statusLabel,
            "Europe/Skopje");

    /// <summary>A year of daily trend points and a long activity feed, for the pagination tests.</summary>
    public static AnalyticsReportDto Large(int trendDays = 365, int activityEntries = 100)
    {
        var start = new DateOnly(2026, 1, 1);
        var trend = Enumerable.Range(0, trendDays)
            .Select(i => new TrendPointDto(start.AddDays(i), (i * 7 % 23) + 1, i % 5))
            .ToList();

        var activity = Enumerable.Range(0, activityEntries)
            .Select(i => new ActivityEntryDto(
                "BookingConfirmed",
                $"Booking confirmed for guest {i}",
                GeneratedAt.AddMinutes(-i),
                null,
                null))
            .ToList();

        var pages = Enumerable.Range(0, 12)
            .Select(i => new BookingPagePerformanceDto(
                Guid.NewGuid(), $"Booking page {i}", $"page-{i}", i % 2 == 0,
                Views: 100 - i, Bookings: 40 - i, Cancelled: i, Upcoming: i,
                ConversionRate: 0.4, CancellationRate: 0.1))
            .ToList();

        return Populated(trend: trend, pages: pages, activity: activity);
    }
}
