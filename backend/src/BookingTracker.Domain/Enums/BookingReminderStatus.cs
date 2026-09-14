namespace BookingTracker.Domain.Enums;

/// <summary>
/// The *scheduling* lifecycle of a reminder - deliberately NOT its delivery
/// outcome. Once a reminder reaches <see cref="Queued"/> its fate belongs to
/// the notification it produced (EmailNotification.Status: Pending/Sent/Failed),
/// which is the single source of truth for whether the message actually went
/// out, how many attempts it took, and why it failed. Storing "Sent" here too
/// would be a second copy of that fact and would drift the first time a send
/// is retried; the reminder read model joins the two instead.
/// </summary>
public enum BookingReminderStatus
{
    /// <summary>Materialized and waiting for its send time to arrive.</summary>
    Scheduled = 0,

    /// <summary>Handed to a notification channel (today: the email queue). Delivery status lives on that notification.</summary>
    Queued = 1,

    /// <summary>The booking was cancelled, or rescheduled away from this reminder's target time, before it was queued.</summary>
    Cancelled = 2,

    /// <summary>Its send time passed while the app was down, by more than the configured grace period - too late to still be useful.</summary>
    Skipped = 3
}
