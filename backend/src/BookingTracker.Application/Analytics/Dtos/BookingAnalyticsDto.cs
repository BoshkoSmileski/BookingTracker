namespace BookingTracker.Application.Analytics.Dtos;

/// <summary>
/// Everything the main dashboard renders from booking data: headline counts, the
/// trend line, the status split, weekday/hour distributions, completion timings,
/// and per-page performance. One DTO (and one round trip) because the dashboard
/// shows all of it at once and every part shares the same filter - splitting it
/// would mean several requests that must agree on the same window.
/// </summary>
public record BookingAnalyticsDto(
    BookingSummaryDto Summary,
    IReadOnlyList<TrendPointDto> Trend,
    IReadOnlyList<StatusSliceDto> StatusDistribution,
    IReadOnlyList<WeekdayCountDto> BookingsByWeekday,
    IReadOnlyList<HourCountDto> BookingsByHour,
    CompletionTimeDto CompletionTime,
    IReadOnlyList<BookingPagePerformanceDto> PagePerformance,
    string TimeZoneId);

/// <param name="TotalSessions">Every booking session started in the window - the "visitors" number.</param>
/// <param name="Confirmed">Sessions that became a booking (Submitted), including ones later completed.</param>
/// <param name="Rescheduled">Bookings moved at least once. Deliberately NOT a slice of the status chart - a booking can be rescheduled AND upcoming, so it overlaps every other category.</param>
/// <param name="CompletionRate">Confirmed / TotalSessions, 0-1. The share of visitors who finished booking.</param>
public record BookingSummaryDto(
    int TotalSessions,
    int Confirmed,
    int Cancelled,
    int Abandoned,
    int InProgress,
    int Upcoming,
    int Completed,
    int Rescheduled,
    int TotalBookingPages,
    int ActiveBookingPages,
    double CompletionRate);

public record TrendPointDto(DateOnly Date, int Sessions, int Bookings);

public record StatusSliceDto(string Status, int Count, double Share);

public record WeekdayCountDto(int DayOfWeek, string Label, int Count);

public record HourCountDto(int Hour, int Count);

/// <summary>Durations in seconds from session start to submit. Null when no booking in scope was completed.</summary>
public record CompletionTimeDto(
    double? AverageSeconds,
    double? MedianSeconds,
    double? FastestSeconds,
    double? SlowestSeconds,
    int SampleSize);

public record BookingPagePerformanceDto(
    Guid BookingPageId,
    string Title,
    string Slug,
    bool IsActive,
    int Views,
    int Bookings,
    int Cancelled,
    int Upcoming,
    double ConversionRate,
    double CancellationRate);
