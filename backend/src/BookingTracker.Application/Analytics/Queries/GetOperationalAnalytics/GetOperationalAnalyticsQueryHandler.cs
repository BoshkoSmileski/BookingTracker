using BookingTracker.Application.Analytics.Dtos;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Notifications;
using BookingTracker.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.Analytics.Queries.GetOperationalAnalytics;

/// <summary>
/// Delivery health: outbound email, reminders, calendar sync. All three read the
/// tables those features already write - EmailNotifications, BookingReminders,
/// CalendarConnection/CalendarSyncedEvents - with no additional bookkeeping.
/// Each is a grouped aggregate whose result set is a handful of rows.
/// </summary>
public class GetOperationalAnalyticsQueryHandler : IRequestHandler<GetOperationalAnalyticsQuery, OperationalAnalyticsDto>
{
    private readonly IBookingTrackerDbContext _db;

    public GetOperationalAnalyticsQueryHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<OperationalAnalyticsDto> Handle(GetOperationalAnalyticsQuery request, CancellationToken cancellationToken)
    {
        var scope = await AnalyticsScope.ResolveAsync(_db, request.Filter, cancellationToken);
        var sessionIds = scope.Sessions.Select(s => s.Id);

        return new OperationalAnalyticsDto(
            await BuildEmailAsync(sessionIds, cancellationToken),
            await BuildRemindersAsync(sessionIds, cancellationToken),
            await BuildCalendarAsync(scope, request.Filter, cancellationToken));
    }

    private async Task<EmailAnalyticsDto> BuildEmailAsync(IQueryable<Guid> sessionIds, CancellationToken cancellationToken)
    {
        // Grouped by (type, status): at most ~8 types x 3 statuses, so the result set
        // is tiny and every count is computed by SQL.
        var grouped = await _db.EmailNotifications.AsNoTracking()
            .Where(n => n.BookingSessionId != null && sessionIds.Contains(n.BookingSessionId!.Value))
            .GroupBy(n => new { n.NotificationType, n.Status })
            .Select(g => new
            {
                g.Key.NotificationType,
                g.Key.Status,
                Count = g.Count(),
                Attempts = g.Sum(n => n.AttemptCount),
            })
            .ToListAsync(cancellationToken);

        var total = grouped.Sum(g => g.Count);
        var sent = grouped.Where(g => g.Status == EmailNotificationStatus.Sent).Sum(g => g.Count);
        var failed = grouped.Where(g => g.Status == EmailNotificationStatus.Failed).Sum(g => g.Count);
        var pending = grouped.Where(g => g.Status == EmailNotificationStatus.Pending).Sum(g => g.Count);

        var byType = grouped
            .GroupBy(g => g.NotificationType)
            .Select(g => new EmailTypeCountDto(
                g.Key.ToString(),
                g.Sum(x => x.Count),
                g.Where(x => x.Status == EmailNotificationStatus.Sent).Sum(x => x.Count),
                g.Where(x => x.Status == EmailNotificationStatus.Failed).Sum(x => x.Count)))
            .OrderByDescending(t => t.Total)
            .ToList();

        // Delivery rate is measured against resolved notifications only - counting
        // still-pending ones as failures would make a healthy queue look broken.
        var resolved = sent + failed;

        return new EmailAnalyticsDto(
            total, pending, sent, failed,
            grouped.Sum(g => g.Attempts),
            resolved == 0 ? 0 : (double)sent / resolved,
            byType);
    }

    private async Task<ReminderAnalyticsDto> BuildRemindersAsync(IQueryable<Guid> sessionIds, CancellationToken cancellationToken)
    {
        // Grouped by (scheduling status, delivery status) so the outcome can be
        // resolved by the shared ReminderOutcome rule without materializing rows -
        // the grouped result is at most a dozen rows.
        var grouped = await _db.BookingReminders.AsNoTracking()
            .Where(r => sessionIds.Contains(r.BookingSessionId))
            .GroupJoin(
                _db.EmailNotifications.AsNoTracking(),
                r => r.EmailNotificationId,
                n => n.Id,
                (r, notifications) => new { Reminder = r, Notifications = notifications })
            .SelectMany(x => x.Notifications.DefaultIfEmpty(), (x, n) => new { x.Reminder.Status, DeliveryStatus = (EmailNotificationStatus?)n.Status, x.Reminder.MinutesBeforeEvent })
            .GroupBy(x => new { x.Status, x.DeliveryStatus })
            .Select(g => new
            {
                g.Key.Status,
                g.Key.DeliveryStatus,
                Count = g.Count(),
                LeadMinutes = g.Sum(x => x.MinutesBeforeEvent),
            })
            .ToListAsync(cancellationToken);

        var total = grouped.Sum(g => g.Count);
        if (total == 0) return new ReminderAnalyticsDto(0, 0, 0, 0, 0, 0, null);

        int CountWithOutcome(string outcome) =>
            grouped.Where(g => ReminderOutcome.Resolve(g.Status, g.DeliveryStatus) == outcome).Sum(g => g.Count);

        return new ReminderAnalyticsDto(
            total,
            CountWithOutcome(ReminderOutcome.Scheduled),
            CountWithOutcome(ReminderOutcome.Sent),
            CountWithOutcome(ReminderOutcome.Failed),
            CountWithOutcome(ReminderOutcome.Skipped),
            CountWithOutcome(ReminderOutcome.Cancelled),
            (double)grouped.Sum(g => g.LeadMinutes) / total);
    }

    private async Task<CalendarAnalyticsDto> BuildCalendarAsync(
        AnalyticsScope scope, AnalyticsFilter filter, CancellationToken cancellationToken)
    {
        var connection = await _db.CalendarConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.OrganizerId == filter.OrganizerId, cancellationToken);

        var sessionIds = scope.Sessions.Select(s => s.Id);
        var syncedBookings = await _db.CalendarSyncedEvents.AsNoTracking()
            .CountAsync(e => sessionIds.Contains(e.BookingSessionId), cancellationToken);
        var confirmedBookings = await scope.Sessions
            .CountAsync(s => s.Status == BookingSessionStatus.Submitted, cancellationToken);

        if (connection is null)
        {
            return new CalendarAnalyticsDto(false, null, null, null, null, syncedBookings, confirmedBookings, null, null, null, null);
        }

        return new CalendarAnalyticsDto(
            true,
            connection.ExternalAccountEmail,
            connection.ExternalCalendarName,
            connection.Status.ToString(),
            DescribeHealth(connection.Status),
            syncedBookings,
            confirmedBookings,
            confirmedBookings == 0 ? null : (double)syncedBookings / confirmedBookings,
            connection.LastSuccessfulSyncAtUtc,
            connection.LastFailedSyncAtUtc,
            connection.LastSyncError);
    }

    /// <summary>Same organizer-facing wording the calendar settings page uses for each sync state.</summary>
    private static string DescribeHealth(CalendarSyncStatus status) => status switch
    {
        CalendarSyncStatus.Connected => "Connected",
        CalendarSyncStatus.ReauthorizationRequired => "Needs Reauthentication",
        CalendarSyncStatus.CalendarNotFound => "Calendar Missing",
        _ => "Synchronization Failed",
    };
}
