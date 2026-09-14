namespace BookingTracker.Application.Availability.Dtos;

/// <summary>
/// Local* is organizer-local wall-clock time - exactly what the existing
/// DateSelected/TimeSelected tracking events and BookingSession.SelectedDate/
/// SelectedTime already expect, so picking a slot needs no changes to that
/// pipeline. *Utc is provided purely so the frontend can display the slot in
/// the visitor's own timezone.
/// </summary>
public record AvailableSlotDto(DateOnly LocalDate, TimeOnly LocalStartTime, TimeOnly LocalEndTime, DateTime StartUtc, DateTime EndUtc);
