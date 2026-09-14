using BookingTracker.Application.Analytics.Dtos;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.Analytics.Queries.GetActivityFeed;

/// <summary>
/// Recent organizer activity, merged from sources that already record it: the
/// booking-session event log (confirmed / cancelled / rescheduled / reminder
/// sent / email sent), failed email notifications, and booking-page creation
/// timestamps. No activity table was introduced.
///
/// Each source is queried with its own `Take(limit)` before merging, so the feed
/// costs three small top-N reads regardless of how much history exists.
/// </summary>
public class GetActivityFeedQueryHandler : IRequestHandler<GetActivityFeedQuery, IReadOnlyList<ActivityEntryDto>>
{
    /// <summary>Session events worth surfacing. FieldChanged/idle/date-pick noise is deliberately excluded - this is an activity feed, not the per-session timeline (which already exists on the session page).</summary>
    private static readonly BookingEventType[] InterestingEvents =
    [
        BookingEventType.BookingSubmitted,
        BookingEventType.BookingCancelled,
        BookingEventType.BookingRescheduled,
        BookingEventType.ReminderSent,
        BookingEventType.EmailSent,
    ];

    private readonly IBookingTrackerDbContext _db;

    public GetActivityFeedQueryHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<IReadOnlyList<ActivityEntryDto>> Handle(GetActivityFeedQuery request, CancellationToken cancellationToken)
    {
        var scope = await AnalyticsScope.ResolveAsync(_db, request.Filter, cancellationToken);
        var limit = Math.Clamp(request.Limit, 1, 100);
        var sessionIds = scope.Sessions.Select(s => s.Id);

        var sessionEvents = await _db.BookingSessionEvents.AsNoTracking()
            .Where(e => sessionIds.Contains(e.SessionId) && InterestingEvents.Contains(e.EventType))
            .OrderByDescending(e => e.Timestamp)
            .Take(limit)
            .Select(e => new { e.EventType, e.FieldName, e.NewValue, e.Timestamp, e.BookingPageId, e.SessionId })
            .ToListAsync(cancellationToken);

        var failedEmails = await _db.EmailNotifications.AsNoTracking()
            .Where(n => n.Status == EmailNotificationStatus.Failed
                && n.BookingSessionId != null && sessionIds.Contains(n.BookingSessionId!.Value))
            .OrderByDescending(n => n.CreatedAtUtc)
            .Take(limit)
            .Select(n => new { n.NotificationType, n.ToEmail, n.CreatedAtUtc, n.BookingSessionId })
            .ToListAsync(cancellationToken);

        var pages = await _db.BookingPages.AsNoTracking()
            .Where(p => p.OrganizerId == request.Filter.OrganizerId && scope.PageIds.Contains(p.Id))
            .OrderByDescending(p => p.CreatedAt)
            .Take(limit)
            .Select(p => new { p.Id, p.Title, p.CreatedAt })
            .ToListAsync(cancellationToken);

        var entries = new List<ActivityEntryDto>(sessionEvents.Count + failedEmails.Count + pages.Count);

        entries.AddRange(sessionEvents.Select(e => e.EventType switch
        {
            BookingEventType.BookingSubmitted =>
                new ActivityEntryDto("BookingConfirmed", "Booking confirmed", e.Timestamp, e.BookingPageId, e.SessionId),
            BookingEventType.BookingCancelled =>
                new ActivityEntryDto("BookingCancelled", "Booking cancelled", e.Timestamp, e.BookingPageId, e.SessionId),
            BookingEventType.BookingRescheduled =>
                new ActivityEntryDto("BookingRescheduled", "Booking rescheduled", e.Timestamp, e.BookingPageId, e.SessionId),
            BookingEventType.ReminderSent =>
                new ActivityEntryDto("ReminderSent", $"{e.FieldName} reminder sent to {e.NewValue}", e.Timestamp, e.BookingPageId, e.SessionId),
            _ =>
                new ActivityEntryDto("EmailSent", $"{Humanize(e.FieldName)} email sent to {e.NewValue}", e.Timestamp, e.BookingPageId, e.SessionId),
        }));

        entries.AddRange(failedEmails.Select(n => new ActivityEntryDto(
            "EmailFailed", $"{Humanize(n.NotificationType.ToString())} email to {n.ToEmail} failed", n.CreatedAtUtc, null, n.BookingSessionId)));

        entries.AddRange(pages.Select(p => new ActivityEntryDto(
            "BookingPageCreated", $"Booking page \"{p.Title}\" created", p.CreatedAt, p.Id, null)));

        return entries
            .OrderByDescending(e => e.OccurredAtUtc)
            .Take(limit)
            .ToList();
    }

    /// <summary>"BookingConfirmation" -> "Booking confirmation", so template/enum names read as prose in the feed.</summary>
    private static string Humanize(string? pascalCase)
    {
        if (string.IsNullOrWhiteSpace(pascalCase)) return "Notification";
        var spaced = string.Concat(pascalCase.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + char.ToLowerInvariant(c) : c.ToString()));
        return char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }
}
