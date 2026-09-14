using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Notifications;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BookingTracker.Infrastructure.BackgroundServices;

/// <summary>
/// Drains the persisted email queue (EmailNotification rows added by
/// EmailNotificationService) - the "queue work in the background, never send
/// inside a request thread" piece of the notification system. Same polling
/// shape as BookingReminderSweeper/GoogleCalendarSyncSweeper: a scoped sweep
/// on a fixed interval, wrapped in its own try/catch so one bad sweep never
/// kills the hosted service.
///
/// Retry policy lives entirely here (and in EmailNotification.RecordFailedAttempt,
/// which owns the actual backoff math) - IEmailSender implementations never
/// retry internally, so there's exactly one place retry decisions are made.
/// A notification that keeps failing is marked Failed after MaxAttempts and
/// left alone; nothing here ever retries a Failed row.
/// </summary>
public class EmailQueueProcessor : BackgroundService
{
    private const int BatchSize = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EmailNotificationSettings _settings;
    private readonly ILogger<EmailQueueProcessor> _logger;

    public EmailQueueProcessor(
        IServiceScopeFactory scopeFactory, IOptions<EmailNotificationSettings> settings, ILogger<EmailQueueProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _settings.QueuePollIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueNotificationsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email queue sweep failed.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    /// <summary>
    /// One sweep of the queue. Internal rather than private purely so the tests
    /// can drive a real sweep against an InMemory database and a fake sender -
    /// the retry decisions this makes are the kind that only show up under a
    /// failing transport, which is not something a running app reproduces on
    /// demand. Nothing but ExecuteAsync calls it in production.
    /// </summary>
    internal async Task ProcessDueNotificationsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IBookingTrackerDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        var now = DateTime.UtcNow;
        var due = await db.EmailNotifications
            .Where(n => n.Status == EmailNotificationStatus.Pending && n.NextAttemptAtUtc <= now)
            .OrderBy(n => n.NextAttemptAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (due.Count == 0) return;

        var sentCount = 0;
        var failedOrRetryingCount = 0;

        foreach (var notification in due)
        {
            // Stop BEFORE the next send rather than part-way through the batch. A send
            // cannot be taken back, so the only safe place to react to shutdown is
            // between two of them; whatever is left stays Pending and is picked up by
            // the next sweep, which is exactly what Pending means.
            if (cancellationToken.IsCancellationRequested) break;

            // Never log ToEmail's message body - subject and recipient only, per the
            // project's "never log a raw secret/sensitive payload" logging convention.
            try
            {
                // The invitation was rendered and stored when the email was composed;
                // the processor only carries it across, staying unaware of ICS internals.
                var calendar = notification.HasCalendarInvitation
                    ? new EmailCalendarAttachment(notification.IcsFileName!, notification.IcsContent!, notification.IcsMethod!)
                    : null;

                await sender.SendAsync(
                    new EmailMessage(
                        notification.ToEmail, notification.ToName, notification.Subject,
                        notification.HtmlBody, notification.TextBody, calendar),
                    cancellationToken);

                notification.MarkSent();
                sentCount++;
                _logger.LogInformation(
                    "Sent {NotificationType} email to {ToEmail} (attempt {AttemptNumber}).",
                    notification.NotificationType, notification.ToEmail, notification.AttemptCount + 1);

                if (notification.BookingSessionId is { } sessionId)
                {
                    await LogEmailSentEventAsync(db, sessionId, notification, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                var baseDelay = TimeSpan.FromSeconds(Math.Max(1, _settings.RetryDelaySeconds));

                // Whether this failure could ever succeed on a retry is a
                // transport question, so it is answered here where the SMTP
                // reply code is still visible. What that answer DOES - fail now
                // versus back off and try again - stays in RecordFailedAttempt,
                // which remains the only place retry policy lives.
                var kind = SmtpFailureClassifier.Classify(ex);
                notification.RecordFailedAttempt(ex.Message, baseDelay, kind);
                failedOrRetryingCount++;

                if (kind == EmailFailureKind.Permanent)
                {
                    // Called out separately from "ran out of attempts" because the
                    // fix is completely different: this one is the SMTP
                    // configuration, and no amount of waiting will clear it.
                    _logger.LogError(
                        ex, "Permanently failed to send {NotificationType} email to {ToEmail}: the SMTP server rejected this session's authentication, so no retry was attempted. Check the Email:Username/Password configuration.",
                        notification.NotificationType, notification.ToEmail);
                }
                else if (notification.Status == EmailNotificationStatus.Failed)
                {
                    _logger.LogError(
                        ex, "Permanently failed to send {NotificationType} email to {ToEmail} after {AttemptCount} attempt(s).",
                        notification.NotificationType, notification.ToEmail, notification.AttemptCount);
                }
                else
                {
                    _logger.LogWarning(
                        ex, "Failed to send {NotificationType} email to {ToEmail} (attempt {AttemptCount} of {MaxAttempts}); retrying at {NextAttemptAtUtc:O}.",
                        notification.NotificationType, notification.ToEmail, notification.AttemptCount, notification.MaxAttempts, notification.NextAttemptAtUtc);
                }
            }

            // Persisted per notification, not once per batch, and deliberately NOT with
            // the sweep's cancellation token. SendAsync has already handed the message to
            // the transport - that is irreversible - so this row is the only record that
            // it happened. Saving the whole batch at the end meant a shutdown mid-drain
            // threw every one of those records away and re-sent up to BatchSize emails on
            // the next start; cancelling this particular save would do the same thing one
            // row at a time. The cost is one round trip per message instead of per batch,
            // which is nothing next to an SMTP conversation.
            await db.SaveChangesAsync(CancellationToken.None);
        }

        if (sentCount > 0 || failedOrRetryingCount > 0)
        {
            _logger.LogInformation("Email queue sweep: {SentCount} sent, {FailedOrRetryingCount} failed/retrying.", sentCount, failedOrRetryingCount);
        }
    }

    /// <summary>
    /// Mirrors the existing "email history is just a filter over the session's
    /// own event log" approach (see BookingSession.LogEmailSent/LogReminderSent) -
    /// no separate notification-log table, so GetEmailHistory keeps working
    /// unchanged even though sending is now asynchronous.
    /// </summary>
    private static async Task LogEmailSentEventAsync(
        IBookingTrackerDbContext db, Guid sessionId, EmailNotification notification, CancellationToken cancellationToken)
    {
        var session = await db.BookingSessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null) return;

        var @event = notification.NotificationType == EmailNotificationType.Reminder
            ? session.LogReminderSent(notification.EventLogFieldName, notification.ToEmail)
            : session.LogEmailSent(notification.EventLogFieldName, notification.ToEmail);

        db.BookingSessionEvents.Add(@event);
    }
}
