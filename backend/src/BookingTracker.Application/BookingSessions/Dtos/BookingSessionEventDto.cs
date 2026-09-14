namespace BookingTracker.Application.BookingSessions.Dtos;

public record BookingSessionEventDto(
    long Id,
    Guid SessionId,
    Guid BookingPageId,
    string EventType,
    string? FieldName,
    string? OldValue,
    string? NewValue,
    int ClientSequenceNumber,
    DateTime Timestamp,
    string? ClientIp,
    string? UserAgent);

/// <summary>
/// Inbound shape for a single interaction reported by the frontend tracker.
/// EventType is a string matching BookingEventType's name so the client never
/// needs to know the numeric enum values.
/// </summary>
public record ClientBookingEventDto(
    string EventType,
    string? FieldName,
    string? NewValue,
    int ClientSequenceNumber);
