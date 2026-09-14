using BookingTracker.Application.Common;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.Analytics;

/// <summary>
/// Resolves an <see cref="AnalyticsFilter"/> into the pieces every analytics
/// handler needs: the organizer's page ids (ownership-scoped), the filtered
/// session query, and the organizer's local "now".
///
/// Exists so the four analytics handlers cannot drift on what "in scope" means -
/// if page filtering or the date-range semantics were re-written per handler,
/// two cards on the same dashboard would silently disagree. Every query it
/// returns is still an <see cref="IQueryable{T}"/>, so callers compose
/// aggregates onto it and the counting happens in SQL, never in memory.
/// </summary>
public sealed class AnalyticsScope
{
    private AnalyticsScope(
        IReadOnlyList<Guid> pageIds, IQueryable<BookingSession> sessions, DateOnly localToday, TimeOnly localTimeNow, string timeZoneId)
    {
        PageIds = pageIds;
        Sessions = sessions;
        LocalToday = localToday;
        LocalTimeNow = localTimeNow;
        TimeZoneId = timeZoneId;
    }

    /// <summary>Booking pages the organizer owns, already narrowed by the filter's BookingPageId if one was given.</summary>
    public IReadOnlyList<Guid> PageIds { get; }

    /// <summary>Sessions in scope - page-scoped, date-range-scoped, status-scoped. Not yet materialized.</summary>
    public IQueryable<BookingSession> Sessions { get; }

    /// <summary>
    /// "Now" in the organizer's own timezone, split into the date and time parts a
    /// booking is actually stored as. Analytics are organizer-scoped, so there is
    /// exactly one timezone in play and the upcoming/completed split can be a plain
    /// SQL comparison against these - no per-row conversion, which SQL could not do.
    /// </summary>
    public DateOnly LocalToday { get; }

    public TimeOnly LocalTimeNow { get; }

    public string TimeZoneId { get; }

    public static async Task<AnalyticsScope> ResolveAsync(
        IBookingTrackerDbContext db, AnalyticsFilter filter, CancellationToken cancellationToken)
    {
        var pageQuery = db.BookingPages.AsNoTracking().Where(p => p.OrganizerId == filter.OrganizerId);

        if (filter.BookingPageId is { } pageId)
        {
            // Ownership is enforced by the OrganizerId predicate above: a page the
            // organizer does not own simply is not in the set, and asking for one is a
            // 404 rather than a silently empty dashboard.
            var owned = await pageQuery.AnyAsync(p => p.Id == pageId, cancellationToken);
            if (!owned) throw new NotFoundException(nameof(BookingPage), pageId);
            pageQuery = pageQuery.Where(p => p.Id == pageId);
        }

        var pageIds = await pageQuery.Select(p => p.Id).ToListAsync(cancellationToken);

        var sessions = db.BookingSessions.AsNoTracking().Where(s => pageIds.Contains(s.BookingPageId));

        if (filter.From is { } from)
        {
            var fromUtc = from.ToDateTime(TimeOnly.MinValue);
            sessions = sessions.Where(s => s.CreatedAt >= fromUtc);
        }

        if (filter.To is { } to)
        {
            // Inclusive: "to 2026-08-04" must include everything that happened on the 4th.
            var toExclusiveUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
            sessions = sessions.Where(s => s.CreatedAt < toExclusiveUtc);
        }

        if (filter.Status is { } status)
        {
            sessions = sessions.Where(s => s.Status == status);
        }

        var timeZoneId = await db.WorkingSchedules.AsNoTracking()
            .Where(s => s.OrganizerId == filter.OrganizerId)
            .Select(s => s.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken);

        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BookingScheduleTime.ResolveTimeZone(timeZoneId));

        return new AnalyticsScope(
            pageIds,
            sessions,
            DateOnly.FromDateTime(localNow),
            TimeOnly.FromDateTime(localNow),
            timeZoneId ?? "UTC");
    }
}
