using BookingTracker.Domain.Entities;
using BookingTracker.Domain.ValueObjects;

namespace BookingTracker.UnitTests.TestSupport;

/// <summary>Shorthand for building WorkingDay rows in SlotGenerationService/handler tests without repeating TimeRange.Create boilerplate everywhere.</summary>
public static class WorkingDayFactory
{
    public static WorkingDay Enabled(Guid scheduleId, DayOfWeek day, params (int StartHour, int StartMinute, int EndHour, int EndMinute)[] intervals)
        => WorkingDay.Create(
            scheduleId,
            day,
            isEnabled: true,
            intervals.Select(i => TimeRange.Create(new TimeOnly(i.StartHour, i.StartMinute), new TimeOnly(i.EndHour, i.EndMinute))));

    public static WorkingDay Disabled(Guid scheduleId, DayOfWeek day)
        => WorkingDay.Create(scheduleId, day, isEnabled: false, []);

    /// <summary>Date-specific opening hours, in the same shorthand as <see cref="Enabled"/>.</summary>
    public static AvailabilityOverride Override(
        Guid organizerId, DateOnly date, params (int StartHour, int StartMinute, int EndHour, int EndMinute)[] ranges)
        => AvailabilityOverride.Create(
            organizerId,
            date,
            ranges.Select(r => TimeRange.Create(new TimeOnly(r.StartHour, r.StartMinute), new TimeOnly(r.EndHour, r.EndMinute))));

    /// <summary>An override with no ranges - "closed on this date", which is a different statement from having no override.</summary>
    public static AvailabilityOverride ClosedOverride(Guid organizerId, DateOnly date)
        => AvailabilityOverride.Create(organizerId, date, []);
}
