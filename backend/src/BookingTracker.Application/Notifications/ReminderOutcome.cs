using BookingTracker.Domain.Enums;

namespace BookingTracker.Application.Notifications;

/// <summary>
/// The single definition of "what actually happened to this reminder", combining
/// its scheduling status with the delivery status of the notification it
/// produced. Extracted so the per-reminder read model
/// (BookingReminderMappings.ToDto) and the aggregate reminder analytics agree by
/// construction - two copies of this rule would let a booking's detail page and
/// the dashboard disagree about whether a reminder was sent.
///
/// See BookingReminderStatus for why "Sent" deliberately isn't a scheduling
/// status in the first place.
/// </summary>
public static class ReminderOutcome
{
    public const string Scheduled = "Scheduled";
    public const string Queued = "Queued";
    public const string Sent = "Sent";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
    public const string Skipped = "Skipped";

    /// <param name="deliveryStatus">Status of the linked EmailNotification, or null when the reminder has not been queued (or the notification is gone).</param>
    public static string Resolve(BookingReminderStatus schedulingStatus, EmailNotificationStatus? deliveryStatus) => schedulingStatus switch
    {
        BookingReminderStatus.Cancelled => Cancelled,
        BookingReminderStatus.Skipped => Skipped,
        BookingReminderStatus.Scheduled => Scheduled,
        // Queued: the reminder's own job is done - defer to delivery. A missing
        // notification stays "Queued" rather than claiming an outcome nothing recorded.
        _ => deliveryStatus switch
        {
            EmailNotificationStatus.Sent => Sent,
            EmailNotificationStatus.Failed => Failed,
            _ => Queued,
        },
    };
}
