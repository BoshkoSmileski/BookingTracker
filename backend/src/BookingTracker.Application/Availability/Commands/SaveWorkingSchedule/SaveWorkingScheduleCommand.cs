using BookingTracker.Application.Availability.Dtos;
using MediatR;

namespace BookingTracker.Application.Availability.Commands.SaveWorkingSchedule;

public record WorkingDayInput(DayOfWeek DayOfWeek, bool IsEnabled, IReadOnlyList<TimeRangeDto> Intervals);

/// <summary>
/// Upserts the organizer's entire weekly schedule in one shot rather than
/// separate Create/Update commands - a schedule is a single settings resource
/// per organizer, and the editor UI always saves the whole week at once, so a
/// split Create/Update pair would just be two near-identical code paths.
/// </summary>
public record SaveWorkingScheduleCommand(Guid OrganizerId, string TimeZoneId, IReadOnlyList<WorkingDayInput> Days)
    : IRequest<WorkingScheduleDto>;
