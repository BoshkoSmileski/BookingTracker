using BookingTracker.Domain.Common;
using BookingTracker.Domain.Enums;
using BookingTracker.Domain.Exceptions;

namespace BookingTracker.Domain.Entities;

/// <summary>
/// One scheduled reminder for one booking, materialized the moment the booking
/// is confirmed rather than discovered later by scanning bookings. That choice
/// is what makes the rest of the reminder feature possible: a row that exists
/// before its send time can be shown to the organizer ("upcoming"), cancelled
/// when the booking is cancelled, and regenerated on reschedule - none of which
/// you can do to a reminder that is only implied by a booking's start time.
///
/// Deliberately a separate entity rather than fields on BookingSession, exactly
/// like CalendarSyncedEvent: the event-sourced booking model stays completely
/// unaware of reminders, and the whole feature could be deleted without touching
/// BookingSession/BookingSessionEvent.
///
/// Duplicate delivery (the hard requirement) is prevented on two levels: a
/// filtered unique index on (BookingSessionId, MinutesBeforeEvent) covering only
/// the live statuses, so the database physically cannot hold two active reminders
/// for the same booking and offset no matter how often the sweeper runs or
/// restarts; and <see cref="MarkQueued"/>, which is a one-way transition out of
/// Scheduled.
/// </summary>
public sealed class BookingReminder : Entity<Guid>
{
    public Guid BookingSessionId { get; private set; }
    public Guid BookingPageId { get; private set; }

    /// <summary>Lead time this reminder represents, e.g. 1440 for "24 hours before". Part of the dedup key.</summary>
    public int MinutesBeforeEvent { get; private set; }

    /// <summary>Absolute UTC instant this reminder should fire - the booking's start time minus <see cref="MinutesBeforeEvent"/>.</summary>
    public DateTime ScheduledForUtc { get; private set; }

    /// <summary>The booking's own start time in UTC, kept so the sweeper can tell "reminder is late" from "meeting already started" without re-deriving the timezone conversion.</summary>
    public DateTime MeetingStartsAtUtc { get; private set; }

    public ReminderChannel Channel { get; private set; }
    public BookingReminderStatus Status { get; private set; }

    /// <summary>Set when the reminder is handed to the email queue - the join to delivery status, attempt count, and failure reason.</summary>
    public Guid? EmailNotificationId { get; private set; }

    public DateTime? QueuedAtUtc { get; private set; }

    /// <summary>Why this reminder was cancelled or skipped. Null while Scheduled/Queued.</summary>
    public string? ResolutionReason { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    private BookingReminder() { }

    public static BookingReminder Schedule(
        Guid bookingSessionId, Guid bookingPageId, int minutesBeforeEvent, DateTime meetingStartsAtUtc, ReminderChannel channel = ReminderChannel.Email)
    {
        if (minutesBeforeEvent <= 0)
            throw new DomainException("A reminder's lead time must be greater than zero minutes.");

        return new BookingReminder
        {
            Id = Guid.NewGuid(),
            BookingSessionId = bookingSessionId,
            BookingPageId = bookingPageId,
            MinutesBeforeEvent = minutesBeforeEvent,
            MeetingStartsAtUtc = meetingStartsAtUtc,
            ScheduledForUtc = meetingStartsAtUtc.AddMinutes(-minutesBeforeEvent),
            Channel = channel,
            Status = BookingReminderStatus.Scheduled,
            CreatedAtUtc = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Records that this reminder has been handed to a delivery channel. Only
    /// legal from Scheduled - a second call throws rather than silently queueing
    /// the same reminder twice, so a bug in the sweeper surfaces instead of
    /// double-mailing a guest.
    /// </summary>
    public void MarkQueued(Guid emailNotificationId)
    {
        EnsureScheduled(nameof(MarkQueued));
        Status = BookingReminderStatus.Queued;
        EmailNotificationId = emailNotificationId;
        QueuedAtUtc = DateTime.UtcNow;
    }

    /// <summary>Booking cancelled, or rescheduled away from this target time. No-op if already queued/resolved, since an email that is already out cannot be recalled.</summary>
    public void Cancel(string reason)
    {
        if (Status != BookingReminderStatus.Scheduled) return;
        Status = BookingReminderStatus.Cancelled;
        ResolutionReason = reason;
    }

    /// <summary>Its window passed while nothing was running to act on it, by more than the grace period - see ReminderSettings.GracePeriodMinutes.</summary>
    public void Skip(string reason)
    {
        if (Status != BookingReminderStatus.Scheduled) return;
        Status = BookingReminderStatus.Skipped;
        ResolutionReason = reason;
    }

    /// <summary>True once the send time has arrived (or passed) and nothing has acted on it yet.</summary>
    public bool IsDue(DateTime nowUtc) => Status == BookingReminderStatus.Scheduled && ScheduledForUtc <= nowUtc;

    private void EnsureScheduled(string operation)
    {
        if (Status != BookingReminderStatus.Scheduled)
            throw new DomainException($"Cannot {operation} a reminder that is already {Status}.");
    }
}
