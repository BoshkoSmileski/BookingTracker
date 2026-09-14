namespace BookingTracker.Application.Availability.Dtos;

public record WorkingScheduleDto(Guid Id, Guid OrganizerId, string TimeZoneId, IReadOnlyList<WorkingDayDto> Days);
