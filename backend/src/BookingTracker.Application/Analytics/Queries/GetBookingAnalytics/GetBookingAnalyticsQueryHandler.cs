using System.Globalization;
using BookingTracker.Application.Analytics.Dtos;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.Analytics.Queries.GetBookingAnalytics;

/// <summary>
/// The dashboard's booking-side numbers. Everything is derived from the
/// BookingSessions projection - which is exactly what a projection is for - and
/// every count is a SQL aggregate. The two places that pull rows back
/// (weekday/hour, completion durations) project **two columns of
/// already-filtered bookings**, because the grouping keys they need
/// (DateOnly.DayOfWeek, TimeOnly.Hour, a duration percentile) have no reliable
/// SQL translation; that is a narrow bounded read, not "loading the event stream".
/// </summary>
public class GetBookingAnalyticsQueryHandler : IRequestHandler<GetBookingAnalyticsQuery, BookingAnalyticsDto>
{
    private readonly IBookingTrackerDbContext _db;

    public GetBookingAnalyticsQueryHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<BookingAnalyticsDto> Handle(GetBookingAnalyticsQuery request, CancellationToken cancellationToken)
    {
        var scope = await AnalyticsScope.ResolveAsync(_db, request.Filter, cancellationToken);
        var sessions = scope.Sessions;

        // One grouped round trip for every status count instead of one query per status.
        var statusCounts = await sessions
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int CountOf(BookingSessionStatus status) => statusCounts.FirstOrDefault(x => x.Status == status)?.Count ?? 0;

        var confirmed = CountOf(BookingSessionStatus.Submitted);
        var cancelled = CountOf(BookingSessionStatus.Cancelled);
        var abandoned = CountOf(BookingSessionStatus.Abandoned);
        var inProgress = CountOf(BookingSessionStatus.Active);
        var totalSessions = statusCounts.Sum(x => x.Count);

        // Upcoming vs completed is a wall-clock comparison in the organizer's own
        // timezone - see AnalyticsScope.LocalToday for why one comparison works here.
        var booked = sessions.Where(s => s.Status == BookingSessionStatus.Submitted && s.SelectedDate != null && s.SelectedTime != null);
        var completed = await booked.CountAsync(
            s => s.SelectedDate < scope.LocalToday || (s.SelectedDate == scope.LocalToday && s.SelectedTime < scope.LocalTimeNow),
            cancellationToken);
        var upcoming = confirmed - completed;

        var rescheduled = await sessions.CountAsync(s => s.RescheduleCount > 0, cancellationToken);

        var totalPages = await _db.BookingPages.AsNoTracking()
            .CountAsync(p => p.OrganizerId == request.Filter.OrganizerId, cancellationToken);
        var activePages = await _db.BookingPages.AsNoTracking()
            .CountAsync(p => p.OrganizerId == request.Filter.OrganizerId && p.IsActive, cancellationToken);

        var summary = new BookingSummaryDto(
            totalSessions, confirmed, cancelled, abandoned, inProgress, upcoming, completed, rescheduled,
            totalPages, activePages,
            totalSessions == 0 ? 0 : (double)confirmed / totalSessions);

        var trend = await BuildTrendAsync(sessions, request.Filter, cancellationToken);
        var statusDistribution = BuildStatusDistribution(upcoming, completed, cancelled, abandoned, inProgress);
        var (byWeekday, byHour) = await BuildWhenAsync(booked, cancellationToken);
        var completionTime = await BuildCompletionTimeAsync(sessions, cancellationToken);
        var pagePerformance = await BuildPagePerformanceAsync(scope, request.Filter, cancellationToken);

        return new BookingAnalyticsDto(
            summary, trend, statusDistribution, byWeekday, byHour, completionTime, pagePerformance, scope.TimeZoneId);
    }

    /// <summary>Sessions started and bookings confirmed per calendar day, gap-filled so the line has no holes.</summary>
    private static async Task<IReadOnlyList<TrendPointDto>> BuildTrendAsync(
        IQueryable<Domain.Entities.BookingSession> sessions, AnalyticsFilter filter, CancellationToken cancellationToken)
    {
        var grouped = await sessions
            .GroupBy(s => s.CreatedAt.Date)
            .Select(g => new
            {
                Day = g.Key,
                Sessions = g.Count(),
                Bookings = g.Count(s => s.Status == BookingSessionStatus.Submitted),
            })
            .ToListAsync(cancellationToken);

        var byDay = grouped.ToDictionary(x => DateOnly.FromDateTime(x.Day), x => (x.Sessions, x.Bookings));

        // Gap-fill: a day with no activity must plot as zero, not be skipped - a line
        // that jumps over empty days misrepresents the shape of the trend.
        var from = filter.From ?? (byDay.Count > 0 ? byDay.Keys.Min() : DateOnly.FromDateTime(DateTime.UtcNow));
        var to = filter.To ?? DateOnly.FromDateTime(DateTime.UtcNow);
        if (to < from) to = from;

        var points = new List<TrendPointDto>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var value = byDay.GetValueOrDefault(day);
            points.Add(new TrendPointDto(day, value.Sessions, value.Bookings));
        }
        return points;
    }

    /// <summary>
    /// Mutually exclusive slices only. "Rescheduled" is deliberately absent: a
    /// booking can be rescheduled and also upcoming, so including it would double
    /// count and make the shares meaningless. It is reported as its own headline
    /// number instead.
    /// </summary>
    private static IReadOnlyList<StatusSliceDto> BuildStatusDistribution(
        int upcoming, int completed, int cancelled, int abandoned, int inProgress)
    {
        var slices = new (string Status, int Count)[]
        {
            ("Upcoming", upcoming),
            ("Completed", completed),
            ("Cancelled", cancelled),
            ("Abandoned", abandoned),
            ("In progress", inProgress),
        };

        var total = slices.Sum(s => s.Count);
        return slices
            .Where(s => s.Count > 0)
            .Select(s => new StatusSliceDto(s.Status, s.Count, total == 0 ? 0 : (double)s.Count / total))
            .ToList();
    }

    private static async Task<(IReadOnlyList<WeekdayCountDto>, IReadOnlyList<HourCountDto>)> BuildWhenAsync(
        IQueryable<Domain.Entities.BookingSession> booked, CancellationToken cancellationToken)
    {
        // Two columns of already-filtered bookings. SelectedDate/SelectedTime are
        // stored as organizer-local wall-clock values, so weekday and hour need no
        // timezone conversion at all - they are read straight off the booking.
        var slots = await booked
            .Select(s => new { Date = s.SelectedDate!.Value, Time = s.SelectedTime!.Value })
            .ToListAsync(cancellationToken);

        var weekdayCounts = slots.GroupBy(s => (int)s.Date.DayOfWeek).ToDictionary(g => g.Key, g => g.Count());
        var byWeekday = Enumerable.Range(0, 7)
            .Select(d => new WeekdayCountDto(
                d,
                CultureInfo.InvariantCulture.DateTimeFormat.GetDayName((DayOfWeek)d),
                weekdayCounts.GetValueOrDefault(d)))
            .ToList();

        var hourCounts = slots.GroupBy(s => s.Time.Hour).ToDictionary(g => g.Key, g => g.Count());
        // Every hour that has any booking, plus a contiguous span so the axis reads as
        // a day rather than a sparse scatter of whichever hours happened to be used.
        var minHour = hourCounts.Count == 0 ? 9 : hourCounts.Keys.Min();
        var maxHour = hourCounts.Count == 0 ? 17 : hourCounts.Keys.Max();
        var byHour = Enumerable.Range(minHour, maxHour - minHour + 1)
            .Select(h => new HourCountDto(h, hourCounts.GetValueOrDefault(h)))
            .ToList();

        return (byWeekday, byHour);
    }

    private static async Task<CompletionTimeDto> BuildCompletionTimeAsync(
        IQueryable<Domain.Entities.BookingSession> sessions, CancellationToken cancellationToken)
    {
        // Only the two timestamps, only for sessions that actually completed. A median
        // needs the ordered values, so this materializes rather than aggregating -
        // two columns of completed bookings, not a full session load. Deliberately
        // not EF.Functions.DateDiffSecond: that is a SQL Server-only extension, and
        // Application must not depend on a specific provider (it would also break the
        // InMemory-backed tests).
        var stamps = await sessions
            .Where(s => s.SubmittedAt != null)
            .Select(s => new { s.CreatedAt, SubmittedAt = s.SubmittedAt!.Value })
            .ToListAsync(cancellationToken);

        if (stamps.Count == 0) return new CompletionTimeDto(null, null, null, null, 0);

        var ordered = stamps
            .Select(s => (s.SubmittedAt - s.CreatedAt).TotalSeconds)
            .Where(d => d >= 0)
            .OrderBy(d => d)
            .ToList();

        if (ordered.Count == 0) return new CompletionTimeDto(null, null, null, null, 0);

        var median = ordered.Count % 2 == 1
            ? ordered[ordered.Count / 2]
            : (ordered[(ordered.Count / 2) - 1] + ordered[ordered.Count / 2]) / 2;

        return new CompletionTimeDto(ordered.Average(), median, ordered[0], ordered[^1], ordered.Count);
    }

    private async Task<IReadOnlyList<BookingPagePerformanceDto>> BuildPagePerformanceAsync(
        AnalyticsScope scope, AnalyticsFilter filter, CancellationToken cancellationToken)
    {
        // One grouped aggregate over the scoped sessions, then one lookup of page
        // metadata - two queries total regardless of how many pages exist (no N+1).
        var perPage = await scope.Sessions
            .GroupBy(s => s.BookingPageId)
            .Select(g => new
            {
                BookingPageId = g.Key,
                Views = g.Count(),
                Bookings = g.Count(s => s.Status == BookingSessionStatus.Submitted),
                Cancelled = g.Count(s => s.Status == BookingSessionStatus.Cancelled),
                Upcoming = g.Count(s => s.Status == BookingSessionStatus.Submitted
                    && s.SelectedDate != null
                    && (s.SelectedDate > scope.LocalToday
                        || (s.SelectedDate == scope.LocalToday && s.SelectedTime >= scope.LocalTimeNow))),
            })
            .ToListAsync(cancellationToken);

        var pages = await _db.BookingPages.AsNoTracking()
            .Where(p => p.OrganizerId == filter.OrganizerId && scope.PageIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Title, p.Slug, p.IsActive })
            .ToListAsync(cancellationToken);

        var statsById = perPage.ToDictionary(x => x.BookingPageId);

        return pages
            .Select(p =>
            {
                var s = statsById.GetValueOrDefault(p.Id);
                var views = s?.Views ?? 0;
                var bookings = s?.Bookings ?? 0;
                var cancelledCount = s?.Cancelled ?? 0;
                // Cancellation rate is against bookings that were actually made -
                // dividing by views would conflate "nobody booked" with "everybody cancelled".
                var bookingDenominator = bookings + cancelledCount;
                return new BookingPagePerformanceDto(
                    p.Id, p.Title, p.Slug, p.IsActive,
                    views, bookings, cancelledCount, s?.Upcoming ?? 0,
                    views == 0 ? 0 : (double)bookings / views,
                    bookingDenominator == 0 ? 0 : (double)cancelledCount / bookingDenominator);
            })
            .OrderByDescending(p => p.Bookings)
            .ThenByDescending(p => p.Views)
            .ToList();
    }
}
