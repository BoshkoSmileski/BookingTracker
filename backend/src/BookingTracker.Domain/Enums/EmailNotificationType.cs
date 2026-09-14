namespace BookingTracker.Domain.Enums;

/// <summary>
/// Every kind of outbound notification email the system can queue. Kept
/// separate from BookingEventType (which records what happened to a session)
/// - this enum instead classifies *why an email is being sent*, so the queue
/// processor and IEmailNotificationService can reason about it without
/// parsing template name strings.
/// </summary>
public enum EmailNotificationType
{
    BookingConfirmation = 0,
    OrganizerNewBooking = 1,
    CancellationConfirmation = 2,
    OrganizerCancellationNotice = 3,
    RescheduleConfirmation = 4,
    OrganizerRescheduleNotice = 5,
    Reminder = 6,

    /// <summary>Optional copy to the organizer when a guest reminder goes out - opt-in via NotificationSettings.NotifyOrganizerOnReminderSent.</summary>
    OrganizerReminderNotice = 7
}
