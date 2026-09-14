using BookingTracker.Domain.Entities;

namespace BookingTracker.Application.Common.Interfaces;

/// <summary>
/// The single place that decides which notification emails go out for a
/// booking event and queues them (as EmailNotification rows - see
/// EmailNotificationService) instead of sending anything itself. Handlers
/// (Submit/Cancel/Reschedule/ResendConfirmation) and BookingReminderSweeper
/// all funnel through this instead of building EmailTemplates + calling
/// IEmailSender directly, so "does this organizer want guest/organizer/
/// reminder emails" (NotificationSettings) is checked in exactly one place.
///
/// Every method is called AFTER the triggering change (Submit/Cancel/
/// Reschedule) has already been committed - queueing an email can never roll
/// back or block the booking mutation itself.
/// </summary>
public interface IEmailNotificationService
{
    /// <summary>
    /// Queues the guest confirmation and/or organizer new-booking notice, per NotificationSettings.
    /// <para>
    /// Returns whether the <b>guest's</b> copy was queued - see the note on
    /// <see cref="QueueBookingCancelledAsync"/> for why the three
    /// booking-lifecycle methods report this and what it does (and does not) mean.
    /// </para>
    /// </summary>
    Task<bool> QueueBookingConfirmedAsync(BookingSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues the guest and/or organizer cancellation notice, per NotificationSettings.
    /// <para>
    /// Returns whether the <b>guest's</b> copy was queued. False is an ordinary
    /// outcome, not an error: the organizer may have guest notifications
    /// switched off, or the booking may not be in a sendable state.
    /// </para>
    /// <para>
    /// This is deliberately "queued", never "sent". An EmailNotification row is
    /// all that exists when this returns; EmailQueueProcessor transmits it
    /// later, out of the request, and may retry or ultimately fail. The three
    /// lifecycle handlers put this on BookingConfirmationDto so the guest-facing
    /// screen can say "on its way" only when something actually is - and stay
    /// quiet otherwise, rather than promising an email nobody queued.
    /// </para>
    /// </summary>
    Task<bool> QueueBookingCancelledAsync(BookingSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues the guest and/or organizer reschedule notice, per NotificationSettings.
    /// Returns whether the <b>guest's</b> copy was queued - see <see cref="QueueBookingCancelledAsync"/>.
    /// </summary>
    Task<bool> QueueBookingRescheduledAsync(
        BookingSession session, DateOnly? previousDate, TimeOnly? previousTime, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a single reminder email to the guest, and - if the organizer has
    /// NotificationSettings.NotifyOrganizerOnReminderSent switched on - a copy
    /// notice to the organizer alongside it. Does not re-check
    /// RemindersEnabled: the caller (BookingReminderSweeper) only reaches a
    /// reminder that was scheduled while reminders were enabled, and re-reading
    /// the same settings row here would just be the same query twice.
    ///
    /// Returns the queued guest notification's id so the caller can link the
    /// BookingReminder that produced it to its delivery record (attempt count,
    /// failure reason, sent time all live on the notification). Null means the
    /// booking was not in a sendable state and nothing was queued.
    /// </summary>
    Task<Guid?> QueueReminderAsync(BookingSession session, string windowKey, string windowLabel, CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicit, organizer-triggered resend of the guest confirmation email -
    /// bypasses NotifyGuestOnBooking on purpose, since a manual resend is an
    /// explicit action, not an automatic notification.
    /// </summary>
    Task QueueResendBookingConfirmationAsync(BookingSession session, CancellationToken cancellationToken = default);
}
