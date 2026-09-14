namespace BookingTracker.Domain.ValueObjects;

/// <summary>
/// A single bookable window computed by SlotGenerationService. Never persisted -
/// recomputed on every request from working hours, exceptions, and existing
/// bookings. Local values are in the organizer's timezone (and are what actually
/// gets written to BookingSession.SelectedDate/SelectedTime on submit); the UTC
/// values exist purely so the frontend can render the slot in the visitor's own
/// timezone without the backend needing to know it.
/// </summary>
public sealed record AvailableSlot(
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    DateTime StartUtc,
    DateTime EndUtc);
