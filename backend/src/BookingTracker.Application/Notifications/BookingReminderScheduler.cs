using BookingTracker.Application.Common;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookingTracker.Application.Notifications;

/// <summary>
/// See IBookingReminderScheduler for the contract. Pure Application logic - it
/// only writes BookingReminder rows through IBookingTrackerDbContext and never
/// sends anything; BookingReminderSweeper turns due rows into notifications
/// later.
/// </summary>
public class BookingReminderScheduler : IBookingReminderScheduler
{
    private readonly IBookingTrackerDbContext _db;
    private readonly ILogger<BookingReminderScheduler> _logger;

    public BookingReminderScheduler(IBookingTrackerDbContext db, ILogger<BookingReminderScheduler> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ScheduleForBookingAsync(BookingSession session, CancellationToken cancellationToken = default)
    {
        if (session.Status != BookingSessionStatus.Submitted || session.SelectedDate is null || session.SelectedTime is null)
            return;

        var page = await _db.BookingPages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == session.BookingPageId, cancellationToken);
        if (page is null) return;

        var settings = await GetSettingsAsync(page.OrganizerId, cancellationToken);
        if (!settings.RemindersEnabled) return;

        var timeZoneId = await GetTimeZoneIdAsync(page.OrganizerId, cancellationToken);
        var meetingStartsAtUtc = BookingScheduleTime.ToUtc(session.SelectedDate.Value, session.SelectedTime.Value, timeZoneId);

        // Only Scheduled rows count as "already covered". A Queued row belongs to a
        // previous meeting time (it has already been handed to the mailer and cannot
        // be recalled), so it must not suppress a fresh reminder for the new time -
        // which is exactly the reschedule case. The unique index is filtered to
        // Scheduled for the same reason.
        var liveOffsets = await _db.BookingReminders
            .Where(r => r.BookingSessionId == session.Id && r.Status == BookingReminderStatus.Scheduled)
            .Select(r => r.MinutesBeforeEvent)
            .ToListAsync(cancellationToken);
        var liveOffsetSet = liveOffsets.ToHashSet();

        var now = DateTime.UtcNow;
        var created = 0;

        foreach (var minutes in settings.ReminderMinutesBeforeEvent)
        {
            if (liveOffsetSet.Contains(minutes)) continue;

            // A reminder whose moment has already passed is never created rather than
            // created-then-skipped: someone booking two hours out should not get a row
            // claiming a 24-hour reminder was "missed".
            if (meetingStartsAtUtc.AddMinutes(-minutes) <= now) continue;

            _db.BookingReminders.Add(BookingReminder.Schedule(session.Id, session.BookingPageId, minutes, meetingStartsAtUtc));
            created++;
        }

        if (created == 0) return;

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Scheduled {Count} reminder(s) for session {SessionId} (meeting at {MeetingStartsAtUtc:O}).", created, session.Id, meetingStartsAtUtc);
    }

    public async Task RescheduleForBookingAsync(BookingSession session, CancellationToken cancellationToken = default)
    {
        // Two saves on purpose: the cancels must land before the inserts, or the
        // filtered unique index on Scheduled rows would reject a new reminder for an
        // offset whose old row is still Scheduled in the same change set.
        var cancelled = await CancelScheduledAsync(session.Id, "Booking rescheduled", cancellationToken);
        if (cancelled > 0)
        {
            _logger.LogInformation("Cancelled {Count} reminder(s) for rescheduled session {SessionId}.", cancelled, session.Id);
        }

        await ScheduleForBookingAsync(session, cancellationToken);
    }

    public async Task CancelForBookingAsync(BookingSession session, string reason, CancellationToken cancellationToken = default)
    {
        var cancelled = await CancelScheduledAsync(session.Id, reason, cancellationToken);
        if (cancelled > 0)
        {
            _logger.LogInformation("Cancelled {Count} pending reminder(s) for session {SessionId}: {Reason}", cancelled, session.Id, reason);
        }
    }

    public async Task ResyncForOrganizerAsync(Guid organizerId, CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(organizerId, cancellationToken);
        var wanted = settings.RemindersEnabled ? settings.ReminderMinutesBeforeEvent.ToHashSet() : [];

        var pageIds = await _db.BookingPages.AsNoTracking()
            .Where(p => p.OrganizerId == organizerId)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
        if (pageIds.Count == 0) return;

        // Only bookings that can still be reminded about. Date-level pre-filter keeps
        // the scan small; ScheduleForBookingAsync re-checks the exact instant per booking.
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        var futureSessions = await _db.BookingSessions
            .Where(s => pageIds.Contains(s.BookingPageId)
                && s.Status == BookingSessionStatus.Submitted
                && s.SelectedDate != null && s.SelectedTime != null
                && s.SelectedDate >= today)
            .ToListAsync(cancellationToken);
        if (futureSessions.Count == 0) return;

        var sessionIds = futureSessions.Select(s => s.Id).ToList();
        var scheduled = await _db.BookingReminders
            .Where(r => sessionIds.Contains(r.BookingSessionId) && r.Status == BookingReminderStatus.Scheduled)
            .ToListAsync(cancellationToken);

        var removed = 0;
        foreach (var reminder in scheduled.Where(r => !wanted.Contains(r.MinutesBeforeEvent)))
        {
            reminder.Cancel("Reminder interval removed from notification settings");
            removed++;
        }

        // Saved before scheduling so the filtered unique index sees the cancellations
        // first - same ordering constraint as RescheduleForBookingAsync.
        if (removed > 0) await _db.SaveChangesAsync(cancellationToken);

        foreach (var session in futureSessions)
        {
            await ScheduleForBookingAsync(session, cancellationToken);
        }

        _logger.LogInformation(
            "Resynced reminders for organizer {OrganizerId} across {SessionCount} future booking(s); {RemovedCount} no longer configured.",
            organizerId, futureSessions.Count, removed);
    }

    private async Task<int> CancelScheduledAsync(Guid sessionId, string reason, CancellationToken cancellationToken)
    {
        var scheduled = await _db.BookingReminders
            .Where(r => r.BookingSessionId == sessionId && r.Status == BookingReminderStatus.Scheduled)
            .ToListAsync(cancellationToken);

        if (scheduled.Count == 0) return 0;

        foreach (var reminder in scheduled) reminder.Cancel(reason);
        await _db.SaveChangesAsync(cancellationToken);
        return scheduled.Count;
    }

    private async Task<NotificationSettings> GetSettingsAsync(Guid organizerId, CancellationToken cancellationToken)
    {
        var settings = await _db.NotificationSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizerId == organizerId, cancellationToken);
        return settings ?? NotificationSettings.CreateDefault(organizerId);
    }

    private Task<string?> GetTimeZoneIdAsync(Guid organizerId, CancellationToken cancellationToken) =>
        _db.WorkingSchedules.AsNoTracking()
            .Where(s => s.OrganizerId == organizerId)
            .Select(s => s.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken);
}
