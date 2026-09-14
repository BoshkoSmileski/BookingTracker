using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Notifications;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BookingTracker.Infrastructure.BackgroundServices;

/// <summary>
/// Fires due reminders. Previously this scanned every upcoming booking on each
/// sweep and worked out from scratch which lead times had elapsed; now the
/// reminders already exist as rows (created by IBookingReminderScheduler when
/// the booking was confirmed), so a sweep is a narrow indexed query for
/// "Scheduled and due" plus one batched load of the bookings behind them.
///
/// What each sweep does with a due reminder:
///  - booking no longer Submitted (cancelled since scheduling) -> Cancelled
///  - meeting already started, or the send time passed by more than the
///    configured grace period -> Skipped, so a restart after downtime delivers
///    the still-useful reminders and quietly drops the stale ones
///  - otherwise -> queue a notification and transition Scheduled -> Queued
///
/// Nothing here decides *which* reminders a booking gets - that is the
/// scheduler's job, and keeping it there is what makes cancellation and
/// rescheduling work without this class knowing about either.
/// </summary>
public class BookingReminderSweeper : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ReminderSettings _settings;
    private readonly ILogger<BookingReminderSweeper> _logger;

    public BookingReminderSweeper(
        IServiceScopeFactory scopeFactory, IOptions<ReminderSettings> settings, ILogger<BookingReminderSweeper> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _settings.SweepIntervalSeconds));
        _logger.LogInformation(
            "Booking reminder sweeper started (interval {IntervalSeconds}s, batch {BatchSize}, grace {GraceMinutes}m).",
            interval.TotalSeconds, _settings.BatchSize, _settings.GracePeriodMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Booking reminder sweep failed.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IBookingTrackerDbContext>();
        var emailNotificationService = scope.ServiceProvider.GetRequiredService<IEmailNotificationService>();

        var now = DateTime.UtcNow;
        var graceCutoff = now.AddMinutes(-Math.Max(0, _settings.GracePeriodMinutes));
        // Bounds the scan itself so cost stays flat as reminder history grows - see
        // ReminderSettings.MaxReminderAgeHours for why this is separate from the grace period.
        var scanFloor = now.AddHours(-Math.Max(1, _settings.MaxReminderAgeHours));
        var batchSize = Math.Max(1, _settings.BatchSize);

        var due = await db.BookingReminders
            .Where(r => r.Status == BookingReminderStatus.Scheduled
                && r.ScheduledForUtc <= now
                && r.ScheduledForUtc >= scanFloor)
            .OrderBy(r => r.ScheduledForUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (due.Count == 0) return;

        // One batched load instead of a query per reminder (several reminders for the
        // same booking are the normal case, so this also de-duplicates the lookup).
        var sessionIds = due.Select(r => r.BookingSessionId).Distinct().ToList();
        var sessionById = await db.BookingSessions
            .Where(s => sessionIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var queued = 0;
        var skipped = 0;
        var cancelled = 0;

        foreach (var reminder in due)
        {
            // Stop between reminders rather than part-way through one: QueueReminderAsync
            // commits the notification itself, so the only safe place to react to shutdown
            // is before that call, never after it.
            if (cancellationToken.IsCancellationRequested) break;

            if (!sessionById.TryGetValue(reminder.BookingSessionId, out var session))
            {
                reminder.Cancel("Booking no longer exists");
                cancelled++;
                continue;
            }

            if (session.Status != BookingSessionStatus.Submitted)
            {
                // Belt and braces: CancelBookingCommandHandler already cancels these, so
                // reaching here means that best-effort call failed or the booking changed
                // between scheduling and now. Either way, do not remind about it.
                reminder.Cancel($"Booking is {session.Status}");
                cancelled++;
                continue;
            }

            if (reminder.MeetingStartsAtUtc <= now)
            {
                reminder.Skip("Meeting already started");
                skipped++;
                continue;
            }

            if (reminder.ScheduledForUtc < graceCutoff)
            {
                reminder.Skip($"Missed by more than the {_settings.GracePeriodMinutes}-minute grace period");
                skipped++;
                _logger.LogWarning(
                    "Skipped {Window} reminder for session {SessionId} - due {ScheduledForUtc:O}, now {NowUtc:O}.",
                    ReminderWindow.Key(reminder.MinutesBeforeEvent), session.Id, reminder.ScheduledForUtc, now);
                continue;
            }

            var (windowKey, windowLabel) = ReminderWindow.Format(reminder.MinutesBeforeEvent);

            try
            {
                var notificationId = await emailNotificationService.QueueReminderAsync(session, windowKey, windowLabel, cancellationToken);
                if (notificationId is null)
                {
                    reminder.Cancel("Booking is not in a sendable state");
                    cancelled++;
                    continue;
                }

                // One-way transition, enforced by the entity - a reminder can never be
                // queued twice even if this loop somehow saw it twice.
                reminder.MarkQueued(notificationId.Value);
                queued++;

                // Committed immediately, and deliberately NOT with the sweep's token.
                // QueueReminderAsync has already saved the notification row, so from here
                // the email WILL go out; leaving this transition to the batch save at the
                // end meant a shutdown in between left the reminder Scheduled with its
                // notification already committed, and the next sweep queued a second copy.
                // Neither existing guard catches that - the filtered unique index and
                // MarkQueued both constrain BookingReminders, while the duplicate is an
                // extra row in EmailNotifications.
                await db.SaveChangesAsync(CancellationToken.None);

                var lateBy = now - reminder.ScheduledForUtc;
                if (lateBy > TimeSpan.FromMinutes(5))
                {
                    _logger.LogInformation(
                        "Queued {Window} reminder for session {SessionId} {LateByMinutes:F0} minute(s) late (within grace).",
                        windowKey, session.Id, lateBy.TotalMinutes);
                }
            }
            catch (Exception ex)
            {
                // Left Scheduled deliberately: the next sweep retries it, and the grace
                // period is what eventually stops it retrying forever.
                _logger.LogError(ex, "Failed to queue {Window} reminder for session {SessionId}.", windowKey, session.Id);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Reminder sweep: {Queued} queued, {Skipped} skipped, {Cancelled} cancelled (of {Due} due).",
            queued, skipped, cancelled, due.Count);
    }
}
