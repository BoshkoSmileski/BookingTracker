namespace BookingTracker.Domain.Enums;

/// <summary>
/// How a reminder is delivered. Only <see cref="Email"/> exists today - the
/// enum exists so that scheduling (BookingReminder: when a reminder is due,
/// whether it has fired, whether it was cancelled) stays independent of
/// delivery, the same way CalendarProviderType carried a single Google value
/// while keeping the calendar abstraction honest.
///
/// Adding SMS/push/Slack later is: a new enum value, a nullable dispatch-id
/// column alongside EmailNotificationId, and a branch in the sweeper that
/// picks the channel - no change to when reminders are generated, cancelled,
/// or regenerated, which is where all the actual logic is.
/// </summary>
public enum ReminderChannel
{
    Email = 0
}
