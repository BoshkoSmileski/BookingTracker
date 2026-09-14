using BookingTracker.Domain.Common;

namespace BookingTracker.Domain.Entities;

/// <summary>
/// Maps a BookingSession to the external calendar event created for it, so a
/// reschedule/cancel knows which event to update/remove. Kept as its own
/// entity (rather than fields on BookingSession) so the event-sourced booking
/// model stays entirely unaware of calendar concerns - calendar sync is a
/// pure add-on, not a change to how bookings themselves are modeled.
/// </summary>
public class CalendarSyncedEvent : Entity<Guid>
{
    public Guid BookingSessionId { get; private set; }
    public Guid CalendarConnectionId { get; private set; }
    public string ExternalEventId { get; private set; } = default!;
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private CalendarSyncedEvent() { }

    public static CalendarSyncedEvent Create(Guid bookingSessionId, Guid calendarConnectionId, string externalEventId)
    {
        return new CalendarSyncedEvent
        {
            Id = Guid.NewGuid(),
            BookingSessionId = bookingSessionId,
            CalendarConnectionId = calendarConnectionId,
            ExternalEventId = externalEventId,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void MarkUpdated() => UpdatedAt = DateTime.UtcNow;
}
