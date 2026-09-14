using System.Globalization;
using BookingTracker.Application.Analytics.Dtos;

namespace BookingTracker.Application.Analytics.Export;

/// <summary>
/// Turns an <see cref="AnalyticsReportDto"/> into the ordered list of tables both
/// exports are built from. Pure, static, no I/O - the same place in the layer
/// (and for the same reason) as EmailTemplates and IcsCalendarInvitation.
///
/// Every number here comes straight off a panel DTO the dashboard also renders;
/// nothing is recomputed. The only work done is formatting (see
/// <see cref="ReportFormat"/>) and two subtractions that are presentation, not
/// analytics: a funnel step's drop-off is the previous step's count minus its
/// own, and "resolved" email is sent + failed - both already implied by numbers
/// on the same row.
/// </summary>
public static class AnalyticsReportTables
{
    public static IReadOnlyList<ReportTable> Build(AnalyticsReportDto report) =>
    [
        Summary(report),
        Trend(report.Bookings.Trend),
        StatusDistribution(report.Bookings.StatusDistribution),
        Funnel(report.Funnel),
        Abandonment(report.Funnel.Abandonment),
        AbandonmentByStep(report.Funnel.Abandonment),
        Weekdays(report.Bookings.BookingsByWeekday),
        Hours(report.Bookings.BookingsByHour, report.Bookings.TimeZoneId),
        TimeToBook(report.Bookings.CompletionTime),
        PagePerformance(report.Bookings.PagePerformance),
        Email(report.Operations.Email),
        EmailByType(report.Operations.Email),
        Reminders(report.Operations.Reminders),
        Calendar(report.Operations.Calendar),
        Activity(report.Activity),
    ];

    private static readonly IReadOnlyList<ReportColumn> MetricValue =
    [
        new ReportColumn("Metric", Weight: 2.4),
        new ReportColumn("Value", ReportAlign.Right),
    ];

    private static ReportTable Summary(AnalyticsReportDto report)
    {
        var s = report.Bookings.Summary;
        IReadOnlyList<IReadOnlyList<string>> rows =
        [
            ["Visitors (sessions started)", ReportFormat.Number(s.TotalSessions)],
            ["Bookings confirmed", ReportFormat.Number(s.Confirmed)],
            ["Completion rate (%)", ReportFormat.Percent(s.CompletionRate)],
            ["Upcoming", ReportFormat.Number(s.Upcoming)],
            ["Completed", ReportFormat.Number(s.Completed)],
            ["Cancelled", ReportFormat.Number(s.Cancelled)],
            // Reported as its own figure and never as a share: a booking can be
            // rescheduled and also upcoming, so it overlaps every other category.
            ["Rescheduled", ReportFormat.Number(s.Rescheduled)],
            ["Abandoned", ReportFormat.Number(s.Abandoned)],
            ["In progress", ReportFormat.Number(s.InProgress)],
            ["Booking pages", ReportFormat.Number(s.TotalBookingPages)],
            ["Active booking pages", ReportFormat.Number(s.ActiveBookingPages)],
            ["Average time to book", ReportFormat.Duration(report.Bookings.CompletionTime.AverageSeconds)],
        ];
        return new ReportTable(ReportTableKey.Summary, "Summary", MetricValue, rows);
    }

    private static ReportTable Trend(IReadOnlyList<TrendPointDto> trend) =>
        new(
            ReportTableKey.Trend,
            "Booking trend",
            [
                new ReportColumn("Date", Weight: 2),
                new ReportColumn("Sessions", ReportAlign.Right),
                new ReportColumn("Bookings", ReportAlign.Right),
            ],
            trend.Select(IReadOnlyList<string> (p) =>
                [ReportFormat.Date(p.Date), ReportFormat.Number(p.Sessions), ReportFormat.Number(p.Bookings)]).ToList());

    private static ReportTable StatusDistribution(IReadOnlyList<StatusSliceDto> slices) =>
        new(
            ReportTableKey.StatusDistribution,
            "Status distribution",
            [
                new ReportColumn("Status", Weight: 2),
                new ReportColumn("Sessions", ReportAlign.Right),
                new ReportColumn("Share (%)", ReportAlign.Right),
            ],
            slices.Select(IReadOnlyList<string> (s) =>
                [s.Status, ReportFormat.Number(s.Count), ReportFormat.Percent(s.Share)]).ToList());

    private static ReportTable Funnel(ConversionFunnelDto funnel)
    {
        var rows = new List<IReadOnlyList<string>>(funnel.Steps.Count);
        for (var i = 0; i < funnel.Steps.Count; i++)
        {
            var step = funnel.Steps[i];
            // Drop-off is presentation arithmetic over two counts already on the
            // table, not a new metric - the entry step has nothing to drop from.
            var dropOff = i == 0 ? ReportFormat.EmptyCell : ReportFormat.Number(funnel.Steps[i - 1].Count - step.Count);
            rows.Add([
                step.Step,
                ReportFormat.Number(step.Count),
                ReportFormat.Percent(step.ShareOfEntry),
                ReportFormat.Percent(step.StepConversion),
                dropOff,
            ]);
        }

        return new ReportTable(
            ReportTableKey.Funnel,
            "Conversion funnel",
            [
                new ReportColumn("Step", Weight: 2.2),
                new ReportColumn("Sessions", ReportAlign.Right),
                new ReportColumn("Share of entry (%)", ReportAlign.Right, 1.3),
                new ReportColumn("Step conversion (%)", ReportAlign.Right, 1.3),
                new ReportColumn("Drop-off", ReportAlign.Right),
            ],
            rows);
    }

    private static ReportTable Abandonment(AbandonmentDto abandonment) =>
        new(
            ReportTableKey.Abandonment,
            "Abandonment",
            MetricValue,
            [
                ["Abandoned sessions", ReportFormat.Number(abandonment.TotalAbandoned)],
                ["Abandonment rate (%)", ReportFormat.Percent(abandonment.AbandonmentRate)],
                ["Average furthest step reached", ReportFormat.Decimal(abandonment.AverageStepReached)],
                ["Most common drop-off step", ReportFormat.Text(abandonment.MostCommonStep)],
            ]);

    private static ReportTable AbandonmentByStep(AbandonmentDto abandonment) =>
        new(
            ReportTableKey.AbandonmentByStep,
            "Abandonment by step",
            [
                new ReportColumn("Step", Weight: 2),
                new ReportColumn("Sessions", ReportAlign.Right),
                new ReportColumn("Share (%)", ReportAlign.Right),
            ],
            abandonment.ByStep.Select(IReadOnlyList<string> (s) =>
                [s.Step, ReportFormat.Number(s.Count), ReportFormat.Percent(s.Share)]).ToList());

    private static ReportTable Weekdays(IReadOnlyList<WeekdayCountDto> weekdays) =>
        new(
            ReportTableKey.Weekdays,
            "Bookings by weekday",
            [
                new ReportColumn("Weekday", Weight: 2),
                new ReportColumn("Bookings", ReportAlign.Right),
            ],
            weekdays.Select(IReadOnlyList<string> (d) => [d.Label, ReportFormat.Number(d.Count)]).ToList());

    /// <summary>
    /// Hours are the booking's organizer-local wall clock (SelectedTime), so the
    /// column is labelled with the organizer's zone rather than converted - see
    /// AnalyticsScope for why no conversion is needed or wanted here.
    /// </summary>
    private static ReportTable Hours(IReadOnlyList<HourCountDto> hours, string timeZoneId) =>
        new(
            ReportTableKey.Hours,
            $"Bookings by hour ({timeZoneId})",
            [
                new ReportColumn("Hour", Weight: 2),
                new ReportColumn("Bookings", ReportAlign.Right),
            ],
            hours.Select(IReadOnlyList<string> (h) =>
                [h.Hour.ToString("00", CultureInfo.InvariantCulture) + ":00", ReportFormat.Number(h.Count)]).ToList());

    private static ReportTable TimeToBook(CompletionTimeDto completion) =>
        new(
            ReportTableKey.TimeToBook,
            "Time to book",
            [
                new ReportColumn("Metric", Weight: 2),
                new ReportColumn("Seconds", ReportAlign.Right),
                new ReportColumn("Duration", ReportAlign.Right),
            ],
            [
                ["Average", ReportFormat.Seconds(completion.AverageSeconds), ReportFormat.Duration(completion.AverageSeconds)],
                ["Median", ReportFormat.Seconds(completion.MedianSeconds), ReportFormat.Duration(completion.MedianSeconds)],
                ["Fastest", ReportFormat.Seconds(completion.FastestSeconds), ReportFormat.Duration(completion.FastestSeconds)],
                ["Slowest", ReportFormat.Seconds(completion.SlowestSeconds), ReportFormat.Duration(completion.SlowestSeconds)],
                ["Sample size", ReportFormat.Number(completion.SampleSize), ReportFormat.EmptyCell],
            ]);

    private static ReportTable PagePerformance(IReadOnlyList<BookingPagePerformanceDto> pages) =>
        new(
            ReportTableKey.PagePerformance,
            "Booking page performance",
            [
                new ReportColumn("Booking page", Weight: 2.4),
                new ReportColumn("Slug", Weight: 1.8),
                new ReportColumn("Active", ReportAlign.Right, 0.7),
                new ReportColumn("Views", ReportAlign.Right, 0.8),
                new ReportColumn("Bookings", ReportAlign.Right, 0.9),
                new ReportColumn("Conversion (%)", ReportAlign.Right, 1.2),
                new ReportColumn("Cancelled", ReportAlign.Right, 1),
                new ReportColumn("Cancellation rate (%)", ReportAlign.Right, 1.4),
                new ReportColumn("Upcoming", ReportAlign.Right, 1),
            ],
            pages.Select(IReadOnlyList<string> (p) =>
            [
                p.Title,
                p.Slug,
                ReportFormat.Bool(p.IsActive),
                ReportFormat.Number(p.Views),
                ReportFormat.Number(p.Bookings),
                ReportFormat.Percent(p.ConversionRate),
                ReportFormat.Number(p.Cancelled),
                ReportFormat.Percent(p.CancellationRate),
                ReportFormat.Number(p.Upcoming),
            ]).ToList());

    private static ReportTable Email(EmailAnalyticsDto email) =>
        new(
            ReportTableKey.Email,
            "Email delivery",
            MetricValue,
            [
                ["Notifications", ReportFormat.Number(email.Total)],
                ["Sent", ReportFormat.Number(email.Sent)],
                ["Failed", ReportFormat.Number(email.Failed)],
                ["Pending", ReportFormat.Number(email.Pending)],
                ["Retry attempts", ReportFormat.Number(email.Retries)],
                // Measured over resolved notifications only, so a healthy queue
                // with pending work does not read as a delivery failure.
                ["Delivery rate (%)", ReportFormat.Percent(email.DeliveryRate)],
            ]);

    private static ReportTable EmailByType(EmailAnalyticsDto email) =>
        new(
            ReportTableKey.EmailByType,
            "Email by notification type",
            [
                new ReportColumn("Notification type", Weight: 2.4),
                new ReportColumn("Total", ReportAlign.Right),
                new ReportColumn("Sent", ReportAlign.Right),
                new ReportColumn("Failed", ReportAlign.Right),
            ],
            email.ByType.Select(IReadOnlyList<string> (t) =>
            [
                t.NotificationType,
                ReportFormat.Number(t.Total),
                ReportFormat.Number(t.Sent),
                ReportFormat.Number(t.Failed),
            ]).ToList());

    private static ReportTable Reminders(ReminderAnalyticsDto reminders) =>
        new(
            ReportTableKey.Reminders,
            "Reminders",
            MetricValue,
            [
                ["Reminders", ReportFormat.Number(reminders.Total)],
                ["Sent", ReportFormat.Number(reminders.Sent)],
                ["Upcoming", ReportFormat.Number(reminders.Scheduled)],
                ["Cancelled", ReportFormat.Number(reminders.Cancelled)],
                ["Skipped", ReportFormat.Number(reminders.Skipped)],
                ["Failed", ReportFormat.Number(reminders.Failed)],
                ["Average lead time (minutes)", ReportFormat.Decimal(reminders.AverageLeadTimeMinutes)],
            ]);

    private static ReportTable Calendar(CalendarAnalyticsDto calendar) =>
        new(
            ReportTableKey.Calendar,
            "Calendar sync",
            MetricValue,
            [
                ["Connected", ReportFormat.Bool(calendar.Connected)],
                ["Account", ReportFormat.Text(calendar.AccountEmail)],
                ["Calendar", ReportFormat.Text(calendar.CalendarName)],
                ["Health", ReportFormat.Text(calendar.HealthStatus)],
                ["Synced bookings", ReportFormat.Number(calendar.SyncedBookings)],
                ["Confirmed bookings", ReportFormat.Number(calendar.ConfirmedBookings)],
                // "Coverage", never "success rate": nothing records per-attempt
                // sync outcomes, so a success rate is not derivable from the data.
                ["Sync coverage (%)", ReportFormat.Percent(calendar.SyncCoverage)],
                ["Last successful sync", ReportFormat.Timestamp(calendar.LastSuccessfulSyncAtUtc)],
                ["Last failed sync", ReportFormat.Timestamp(calendar.LastFailedSyncAtUtc)],
                ["Last sync error", ReportFormat.Text(calendar.LastSyncError)],
            ]);

    private static ReportTable Activity(IReadOnlyList<ActivityEntryDto> activity) =>
        new(
            ReportTableKey.Activity,
            "Recent activity",
            [
                new ReportColumn("Occurred (UTC)", Weight: 1.6),
                new ReportColumn("Kind", Weight: 1.4),
                new ReportColumn("Description", Weight: 3.4),
            ],
            activity.Select(IReadOnlyList<string> (e) =>
                [ReportFormat.Timestamp(e.OccurredAtUtc), e.Kind, e.Description]).ToList());
}
