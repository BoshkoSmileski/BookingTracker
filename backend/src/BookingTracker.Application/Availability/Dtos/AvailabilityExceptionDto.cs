namespace BookingTracker.Application.Availability.Dtos;

/// <param name="Date">Inclusive first day.</param>
/// <param name="EndDate">Inclusive last day. Equal to <paramref name="Date"/> for a single-day exception, so a client that ignores it still reads correctly.</param>
/// <param name="TotalDays">Days covered, inclusive of both ends - sent rather than re-derived so the UI and the backend cannot disagree about an off-by-one.</param>
public record AvailabilityExceptionDto(
    Guid Id,
    DateOnly Date,
    DateOnly EndDate,
    int TotalDays,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    string Type,
    string? Reason);
