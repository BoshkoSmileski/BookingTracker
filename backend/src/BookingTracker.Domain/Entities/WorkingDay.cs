using BookingTracker.Domain.Common;
using BookingTracker.Domain.Exceptions;
using BookingTracker.Domain.ValueObjects;

namespace BookingTracker.Domain.Entities;

/// <summary>
/// One day of the week within a WorkingSchedule (e.g. "Monday, 09:00-12:00 and
/// 13:00-17:00"). A day with IsEnabled = false or zero intervals produces no
/// slots. Supports multiple intervals per day so a lunch break is modeled
/// simply as the gap between two intervals, not as a separate concept.
/// </summary>
public class WorkingDay : Entity<Guid>
{
    private readonly List<TimeRange> _intervals = [];

    public Guid WorkingScheduleId { get; private set; }
    public DayOfWeek DayOfWeek { get; private set; }
    public bool IsEnabled { get; private set; }
    public IReadOnlyList<TimeRange> Intervals => _intervals.AsReadOnly();

    private WorkingDay() { }

    public static WorkingDay Create(Guid workingScheduleId, DayOfWeek dayOfWeek, bool isEnabled, IEnumerable<TimeRange> intervals)
    {
        var day = new WorkingDay
        {
            Id = Guid.NewGuid(),
            WorkingScheduleId = workingScheduleId,
            DayOfWeek = dayOfWeek,
            IsEnabled = isEnabled
        };

        foreach (var interval in intervals.OrderBy(i => i.Start))
        {
            day.AddInterval(interval);
        }

        return day;
    }

    public void AddInterval(TimeRange interval)
    {
        if (_intervals.Any(i => i.Overlaps(interval)))
            throw new DomainException($"Interval {interval.Start}-{interval.End} overlaps an existing interval on {DayOfWeek}.");

        // Cloned rather than stored directly: EF Core's owned-type change tracker
        // identifies owned instances by reference, so the same TimeRange instance
        // reused across two different WorkingDay owners (e.g. the same "9-12"
        // interval added to every weekday from a shared local variable) would look
        // like one owned instance shared across owners and corrupt on save - the
        // same failure mode this project already hit once with ClientContext.
        _intervals.Add(interval with { });
    }

    public void SetEnabled(bool isEnabled) => IsEnabled = isEnabled;
}
