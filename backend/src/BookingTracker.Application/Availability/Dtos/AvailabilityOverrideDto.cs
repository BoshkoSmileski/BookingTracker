namespace BookingTracker.Application.Availability.Dtos;

/// <summary>
/// One date's specific opening hours.
/// </summary>
/// <param name="Ranges">
/// The day's open hours, ordered by start. Empty means the day is closed - the
/// same single source of truth the entity uses, rather than a separate flag
/// that could contradict the list beside it.
/// </param>
/// <param name="IsClosed">
/// Sent even though it is derivable from <paramref name="Ranges"/>, for the
/// same reason AvailabilityExceptionDto sends TotalDays: it is the thing the
/// UI actually branches on, and deriving it at each call site is how two
/// screens end up disagreeing about what an empty list means.
/// </param>
public record AvailabilityOverrideDto(
    Guid Id,
    DateOnly Date,
    bool IsClosed,
    IReadOnlyList<TimeRangeDto> Ranges,
    string? Note);
