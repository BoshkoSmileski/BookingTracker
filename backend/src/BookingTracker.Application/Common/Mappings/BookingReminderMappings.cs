using BookingTracker.Application.Notifications;
using BookingTracker.Application.Notifications.Dtos;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;

namespace BookingTracker.Application.Common.Mappings;

public static class BookingReminderMappings
{
    /// <summary>
    /// Flattens the scheduling row and (once it exists) its delivery record into
    /// one organizer-facing status. This is the only place the two vocabularies
    /// meet: BookingReminderStatus never says "Sent" - that fact belongs to the
    /// notification and would drift if copied - so a Queued reminder is resolved
    /// here against the notification it produced.
    /// </summary>
    public static BookingReminderDto ToDto(this BookingReminder reminder, EmailNotification? notification)
    {
        // ReminderOutcome owns this rule so the aggregate analytics resolve it
        // identically - see that class for why it is shared rather than inlined here.
        var status = ReminderOutcome.Resolve(reminder.Status, notification?.Status);

        return new BookingReminderDto(
            reminder.Id,
            reminder.MinutesBeforeEvent,
            ReminderWindow.Label(reminder.MinutesBeforeEvent),
            reminder.ScheduledForUtc,
            reminder.MeetingStartsAtUtc,
            status,
            reminder.Channel.ToString(),
            reminder.QueuedAtUtc,
            notification?.SentAtUtc,
            notification?.AttemptCount ?? 0,
            notification?.LastError,
            reminder.ResolutionReason);
    }
}
