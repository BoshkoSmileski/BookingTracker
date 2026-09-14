using BookingTracker.Application.Availability.Dtos;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.ValueObjects;

namespace BookingTracker.Application.Common.Mappings;

public static class AvailabilityMappings
{
    public static TimeRangeDto ToDto(this TimeRange range) => new(range.Start, range.End);

    public static WorkingDayDto ToDto(this WorkingDay day) => new(
        day.DayOfWeek,
        day.IsEnabled,
        day.Intervals.Select(i => i.ToDto()).ToList());

    public static WorkingScheduleDto ToDto(this WorkingSchedule schedule, IReadOnlyList<WorkingDay> days) => new(
        schedule.Id,
        schedule.OrganizerId,
        schedule.TimeZoneId,
        days.OrderBy(d => d.DayOfWeek).Select(d => d.ToDto()).ToList());

    public static AvailabilityExceptionDto ToDto(this AvailabilityException exception) => new(
        exception.Id,
        exception.Date,
        exception.EndDate,
        exception.TotalDays,
        exception.StartTime,
        exception.EndTime,
        exception.Type.ToString(),
        exception.Reason);

    public static AvailabilityOverrideDto ToDto(this AvailabilityOverride @override) => new(
        @override.Id,
        @override.Date,
        @override.IsClosed,
        @override.Ranges.Select(r => r.ToDto()).ToList(),
        @override.Note);

    public static AvailableSlotDto ToDto(this AvailableSlot slot) => new(
        slot.Date,
        slot.StartTime,
        slot.EndTime,
        slot.StartUtc,
        slot.EndUtc);
}
