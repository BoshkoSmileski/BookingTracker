using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Exceptions;
using BookingTracker.Domain.ValueObjects;

namespace BookingTracker.UnitTests.Domain.Entities;

/// <summary>
/// The invariants on a date's specific hours. Everything about *precedence*
/// lives with the slot algorithm (AvailabilityOverrideSlotGenerationTests);
/// this file is only about what the entity itself refuses to hold.
/// </summary>
public class AvailabilityOverrideTests
{
    private static readonly Guid OrganizerId = Guid.NewGuid();
    private static readonly DateOnly Date = new(2026, 8, 15);

    private static TimeRange Range(int startHour, int endHour) =>
        TimeRange.Create(new TimeOnly(startHour, 0), new TimeOnly(endHour, 0));

    [Fact]
    public void Create_StoresTheRangesOrderedByStart()
    {
        // Ordered on the way in, so every reader - slot generation, the DTO, the
        // organizer's list - sees the same sequence without re-sorting.
        var result = AvailabilityOverride.Create(OrganizerId, Date, [Range(13, 15), Range(9, 12)]);

        Assert.Equal([new TimeOnly(9, 0), new TimeOnly(13, 0)], result.Ranges.Select(r => r.Start));
        Assert.False(result.IsClosed);
    }

    [Fact]
    public void AnOverrideWithNoRanges_IsClosed()
    {
        var result = AvailabilityOverride.Create(OrganizerId, Date, []);

        Assert.True(result.IsClosed);
        Assert.Empty(result.Ranges);
    }

    [Fact]
    public void Create_RejectsOverlappingRanges()
    {
        // Overlapping open hours would emit the same slot twice - the same call
        // WorkingDay.AddInterval makes for the weekly schedule.
        Assert.Throws<DomainException>(() =>
            AvailabilityOverride.Create(OrganizerId, Date, [Range(9, 13), Range(12, 17)]));
    }

    [Fact]
    public void Create_AllowsRangesThatTouchWithoutOverlapping()
    {
        // 09:00-12:00 and 12:00-17:00 share only the boundary instant, and
        // TimeRange is half-open, so this is a legitimate way to split a day.
        var result = AvailabilityOverride.Create(OrganizerId, Date, [Range(9, 12), Range(12, 17)]);

        Assert.Equal(2, result.Ranges.Count);
    }

    [Fact]
    public void Create_RejectsMoreRangesThanTheLimit()
    {
        var tooMany = Enumerable.Range(0, AvailabilityOverride.MaxRanges + 1)
            .Select(i => TimeRange.Create(new TimeOnly(i, 0), new TimeOnly(i, 30)))
            .ToList();

        Assert.Throws<DomainException>(() => AvailabilityOverride.Create(OrganizerId, Date, tooMany));
    }

    [Fact]
    public void Update_ReplacesTheWholeDayRatherThanMerging()
    {
        var result = AvailabilityOverride.Create(OrganizerId, Date, [Range(9, 17)]);

        result.Update([Range(13, 15)], "Conference in the morning");

        Assert.Equal([new TimeOnly(13, 0)], result.Ranges.Select(r => r.Start));
        Assert.Equal("Conference in the morning", result.Note);
        Assert.NotNull(result.UpdatedAt);
    }

    [Fact]
    public void Update_CanCloseADayThatPreviouslyHadHours()
    {
        var result = AvailabilityOverride.Create(OrganizerId, Date, [Range(9, 17)]);

        result.Update([], null);

        Assert.True(result.IsClosed);
    }

    [Fact]
    public void Update_RejectsOverlapsToo()
    {
        var result = AvailabilityOverride.Create(OrganizerId, Date, [Range(9, 17)]);

        Assert.Throws<DomainException>(() => result.Update([Range(9, 13), Range(12, 17)], null));
        // The rejected update leaves the previous hours intact.
        Assert.Equal([new TimeOnly(9, 0)], result.Ranges.Select(r => r.Start));
    }

    [Fact]
    public void Ranges_AreClonedSoOneInstanceIsNeverSharedBetweenOwners()
    {
        // EF Core identifies owned instances by reference; the same TimeRange
        // reaching two owners corrupts tracking on save. This project has
        // already been bitten by that twice (ClientContext, WorkingDay).
        var shared = Range(9, 17);
        var first = AvailabilityOverride.Create(OrganizerId, Date, [shared]);
        var second = AvailabilityOverride.Create(OrganizerId, Date.AddDays(1), [shared]);

        Assert.NotSame(shared, first.Ranges[0]);
        Assert.NotSame(first.Ranges[0], second.Ranges[0]);
        Assert.Equal(first.Ranges[0], second.Ranges[0]);
    }
}
