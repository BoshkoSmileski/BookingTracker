namespace BookingTracker.Application.BookingSessions.Dtos;

/// <summary>
/// A visitor's answer to one custom booking form field.
///
/// Carries the field id, not its label: BookingSessionMappings.ToDto is a pure
/// function of the session and is called from places that have no booking page
/// loaded (the SignalR broadcaster, the append-events handler). Both consumers
/// that display an answer - the public wizard and the organizer's session
/// detail screen - already hold the page's FormFields, so the label is joined
/// there rather than duplicated onto every event broadcast.
/// </summary>
public record BookingSessionAnswerDto(Guid FieldId, string Value);
