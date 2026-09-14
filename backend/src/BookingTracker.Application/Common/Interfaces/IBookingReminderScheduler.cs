using BookingTracker.Domain.Entities;

namespace BookingTracker.Application.Common.Interfaces;

/// <summary>
/// Owns the lifecycle of a booking's scheduled reminders - the single place
/// that decides which BookingReminder rows should exist for a booking and when.
/// Kept separate from IEmailNotificationService on purpose: that service
/// answers "who should be emailed about this event, right now", this one
/// answers "what should fire later, and is that still valid" - and only the
/// latter has to survive cancellation and rescheduling.
///
/// Every method is called AFTER the booking mutation it reacts to has already
/// been committed, in the caller's own best-effort try/catch, so a scheduling
/// failure can never roll back or fail a booking.
/// </summary>
public interface IBookingReminderScheduler
{
    /// <summary>
    /// Materializes the organizer's configured reminders for a newly confirmed
    /// booking. Reminders whose send time has already passed are not created at
    /// all (booking made 2 hours out with a 24-hour reminder configured), and
    /// nothing is created if the organizer has reminders switched off.
    /// Idempotent: re-running never produces a second live reminder for the
    /// same booking and offset.
    /// </summary>
    Task ScheduleForBookingAsync(BookingSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Regenerates reminders after a booking moves: cancels every
    /// still-Scheduled reminder aimed at the old time, then schedules a fresh
    /// set against the new one. Already-queued reminders are left alone - that
    /// mail is already out and cannot be recalled.
    /// </summary>
    Task RescheduleForBookingAsync(BookingSession session, CancellationToken cancellationToken = default);

    /// <summary>Cancels every still-Scheduled reminder for a booking, so a cancelled booking never reminds anyone.</summary>
    Task CancelForBookingAsync(BookingSession session, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-aligns every future booking of one organizer with their current
    /// reminder settings: drops scheduled reminders at offsets they just turned
    /// off, adds ones they just turned on. Without this, editing reminder
    /// settings would only affect bookings made afterwards - a change that
    /// silently does nothing to the bookings already on the calendar reads as a
    /// bug, not a design choice. Past and already-queued reminders are never
    /// touched.
    /// </summary>
    Task ResyncForOrganizerAsync(Guid organizerId, CancellationToken cancellationToken = default);
}
