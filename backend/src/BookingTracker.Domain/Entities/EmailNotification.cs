using BookingTracker.Domain.Common;
using BookingTracker.Domain.Enums;
using BookingTracker.Domain.Exceptions;

namespace BookingTracker.Domain.Entities;

/// <summary>
/// A single queued outbound email - the persisted work item behind the email
/// queue. IEmailNotificationService (Application) creates these instead of
/// sending mail directly; EmailQueueProcessor (Infrastructure, a
/// BackgroundService following the same polling shape as
/// BookingReminderSweeper/GoogleCalendarSyncSweeper) is the only thing that
/// ever transitions one from Pending to Sent or Failed. Booking creation/
/// cancellation/reschedule therefore never waits on - or fails because of -
/// SMTP: the row is committed in the same request, sending happens later.
/// </summary>
public sealed class EmailNotification : Entity<Guid>
{
    private const int MaxErrorLength = 1000;

    public Guid? BookingSessionId { get; private set; }
    public EmailNotificationType NotificationType { get; private set; }
    public string ToEmail { get; private set; } = default!;
    public string ToName { get; private set; } = default!;
    public string Subject { get; private set; } = default!;
    public string HtmlBody { get; private set; } = default!;
    public string TextBody { get; private set; } = default!;

    /// <summary>
    /// The template/window name recorded on the session's event log once this
    /// email is actually sent (BookingSession.LogEmailSent/LogReminderSent),
    /// e.g. "BookingConfirmation" or "24h" - kept here so the queue processor
    /// can log it without needing to re-derive it from NotificationType.
    /// </summary>
    public string EventLogFieldName { get; private set; } = default!;

    /// <summary>
    /// The RFC5545 calendar invitation to attach, rendered at queue time
    /// alongside the bodies. Null for notifications that carry no invitation -
    /// reminders deliberately do not (see IcsCalendarInvitation), and any email
    /// whose invitation failed to generate is queued without one rather than
    /// not queued at all.
    ///
    /// Stored rather than regenerated at send time for the same reason
    /// HtmlBody/TextBody are: the queued email must reflect the booking as it
    /// was when composed. Regenerating during delivery would silently send an
    /// invitation for a state the recipient was never told about.
    /// </summary>
    public string? IcsContent { get; private set; }

    public string? IcsFileName { get; private set; }

    /// <summary>REQUEST or CANCEL - needed by the SMTP sender for the `method=` MIME parameter, which is what tells a client to update versus remove the event.</summary>
    public string? IcsMethod { get; private set; }

    public EmailNotificationStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; }
    public DateTime NextAttemptAtUtc { get; private set; }
    public string? LastError { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }

    public bool HasCalendarInvitation => IcsContent is not null && IcsFileName is not null && IcsMethod is not null;

    private EmailNotification() { }

    public static EmailNotification Create(
        Guid? bookingSessionId, EmailNotificationType notificationType, string toEmail, string toName,
        string subject, string htmlBody, string textBody, string eventLogFieldName, int maxAttempts,
        string? icsContent = null, string? icsFileName = null, string? icsMethod = null)
    {
        if (string.IsNullOrWhiteSpace(toEmail)) throw new DomainException("A queued email must have a recipient address.");
        if (maxAttempts < 1) throw new DomainException("MaxAttempts must be at least 1.");

        // All three or none: a half-populated attachment would reach the sender as a
        // nameless or method-less calendar part, which clients reject silently.
        var icsParts = new[] { icsContent, icsFileName, icsMethod };
        if (icsParts.Any(p => p is not null) && icsParts.Any(p => p is null))
            throw new DomainException("A calendar invitation needs content, a file name, and a method together.");

        return new EmailNotification
        {
            Id = Guid.NewGuid(),
            BookingSessionId = bookingSessionId,
            NotificationType = notificationType,
            ToEmail = toEmail,
            ToName = toName,
            Subject = subject,
            HtmlBody = htmlBody,
            TextBody = textBody,
            EventLogFieldName = eventLogFieldName,
            IcsContent = icsContent,
            IcsFileName = icsFileName,
            IcsMethod = icsMethod,
            Status = EmailNotificationStatus.Pending,
            AttemptCount = 0,
            MaxAttempts = maxAttempts,
            NextAttemptAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
        };
    }

    public void MarkSent()
    {
        Status = EmailNotificationStatus.Sent;
        SentAtUtc = DateTime.UtcNow;
        LastError = null;
    }

    /// <summary>
    /// Records one failed send attempt. Backs off exponentially from
    /// <paramref name="baseRetryDelay"/> (1x, 2x, 4x, ... the base delay for
    /// each successive attempt) until MaxAttempts is reached, at which point
    /// the notification is permanently marked Failed and the processor stops
    /// retrying it.
    ///
    /// A <see cref="EmailFailureKind.Permanent"/> failure skips straight to
    /// Failed on the first attempt, because retrying it cannot change the
    /// outcome - see EmailFailureKind for why that matters and why Transient
    /// is the default. The attempt is still counted and the error still
    /// recorded: the row must say honestly that a send was tried and did not
    /// succeed, never that none was made.
    /// </summary>
    public void RecordFailedAttempt(
        string error, TimeSpan baseRetryDelay, EmailFailureKind kind = EmailFailureKind.Transient)
    {
        AttemptCount++;
        LastError = Truncate(error, MaxErrorLength);

        if (kind == EmailFailureKind.Permanent || AttemptCount >= MaxAttempts)
        {
            Status = EmailNotificationStatus.Failed;
            return;
        }

        var backoffMultiplier = Math.Pow(2, AttemptCount - 1);
        var delay = TimeSpan.FromTicks((long)(baseRetryDelay.Ticks * backoffMultiplier));
        NextAttemptAtUtc = DateTime.UtcNow.Add(delay);
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
