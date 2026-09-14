namespace BookingTracker.Application.Analytics.Dtos;

/// <summary>
/// One line in the recent-activity feed. Assembled from sources that already
/// exist - the booking-session event log, failed email notifications, and
/// booking-page creation timestamps - rather than a new activity table.
/// </summary>
/// <param name="Kind">Machine-readable category the UI maps to an icon: BookingConfirmed, BookingCancelled, BookingRescheduled, ReminderSent, EmailSent, EmailFailed, CalendarSynced, BookingPageCreated.</param>
/// <param name="Description">Ready-to-render sentence; the UI does not re-derive wording from Kind.</param>
/// <param name="SessionId">Set when the entry links to a booking, so the UI can deep-link to the session. Null for page-level entries.</param>
public record ActivityEntryDto(
    string Kind,
    string Description,
    DateTime OccurredAtUtc,
    Guid? BookingPageId,
    Guid? SessionId);
