namespace BookingTracker.Application.Notifications;

/// <summary>
/// The email queue's retry policy - bound from the same "Email" configuration
/// section as Infrastructure's EmailSettings (SMTP mechanics). Kept as a
/// separate, Application-owned POCO rather than referencing Infrastructure's
/// EmailSettings directly, since Application may only depend on types under
/// Application/Common/Interfaces. Both classes are bound from the same
/// section in Infrastructure's DependencyInjection - each just reads the keys
/// it declares.
/// </summary>
public class EmailNotificationSettings
{
    public const string SectionName = "Email";

    /// <summary>How many times the queue processor will attempt to send a given email before marking it permanently Failed.</summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>Base delay before the first retry; each subsequent retry doubles it (exponential backoff) - see EmailNotification.RecordFailedAttempt.</summary>
    public int RetryDelaySeconds { get; set; } = 60;

    /// <summary>How often EmailQueueProcessor polls for due, pending emails.</summary>
    public int QueuePollIntervalSeconds { get; set; } = 15;
}
