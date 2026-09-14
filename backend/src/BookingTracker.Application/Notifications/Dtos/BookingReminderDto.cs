namespace BookingTracker.Application.Notifications.Dtos;

/// <summary>
/// One reminder as the organizer sees it, merging the two halves the system
/// deliberately keeps apart: scheduling facts from BookingReminder (when it is
/// due, whether it was cancelled/skipped) and delivery facts from the
/// EmailNotification it produced (attempt count, failure reason, sent time).
/// See BookingReminderMappings for how the two are combined.
/// </summary>
/// <param name="Label">Human label for the lead time, e.g. "24 hours" - the same wording used in the email subject.</param>
/// <param name="Status">Flattened outcome: Scheduled | Queued | Sent | Failed | Cancelled | Skipped.</param>
/// <param name="AttemptCount">Delivery attempts made so far; 0 until the reminder is queued.</param>
/// <param name="FailureReason">Last delivery error, from the notification. Null unless a send has failed.</param>
/// <param name="ResolutionReason">Why it was cancelled or skipped (e.g. "Booking rescheduled"). Null otherwise.</param>
public record BookingReminderDto(
    Guid Id,
    int MinutesBeforeEvent,
    string Label,
    DateTime ScheduledForUtc,
    DateTime MeetingStartsAtUtc,
    string Status,
    string Channel,
    DateTime? QueuedAtUtc,
    DateTime? SentAtUtc,
    int AttemptCount,
    string? FailureReason,
    string? ResolutionReason);
