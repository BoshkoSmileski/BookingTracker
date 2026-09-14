namespace BookingTracker.Application.Availability.Dtos;

public record WorkingDayDto(DayOfWeek DayOfWeek, bool IsEnabled, IReadOnlyList<TimeRangeDto> Intervals);
