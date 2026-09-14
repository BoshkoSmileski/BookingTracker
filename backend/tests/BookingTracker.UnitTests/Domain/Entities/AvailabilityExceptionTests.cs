using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.Domain.Exceptions;

namespace BookingTracker.UnitTests.Domain.Entities;

/// <summary>
/// An exception spans an inclusive date range, so a vacation is one row rather
/// than fourteen. These pin the boundary semantics every consumer depends on -
/// slot generation asks Covers() and nothing else knows where the edges are.
/// </summary>
public class AvailabilityExceptionTests
{
    private static readonly DateOnly Start = new(2026, 8, 4);
    private static readonly Guid OrganizerId = Guid.NewGuid();

    private static AvailabilityException Create(DateOnly? endDate = null, TimeOnly? from = null, TimeOnly? to = null) =>
        AvailabilityException.Create(OrganizerId, Start, from, to, AvailabilityExceptionType.Vacation, "Summer break", endDate);

    [Fact]
    public void WithoutAnEndDate_IsASingleDay()
    {
        var exception = Create();

        // Ranges are additive: every caller that predates them still gets one day.
        Assert.Equal(Start, exception.EndDate);
        Assert.True(exception.IsSingleDay);
        Assert.Equal(1, exception.TotalDays);
    }

    [Fact]
    public void WithAnEndDate_CoversBothEndsAndEverythingBetween()
    {
        var exception = Create(endDate: new DateOnly(2026, 8, 17));

        Assert.False(exception.IsSingleDay);
        Assert.Equal(14, exception.TotalDays);
        Assert.True(exception.Covers(Start));                        // inclusive first day
        Assert.True(exception.Covers(new DateOnly(2026, 8, 11)));
        Assert.True(exception.Covers(new DateOnly(2026, 8, 17)));     // inclusive last day
        Assert.False(exception.Covers(new DateOnly(2026, 8, 3)));
        Assert.False(exception.Covers(new DateOnly(2026, 8, 18)));
    }

    [Fact]
    public void EndDateEqualToStart_IsStillASingleDay()
    {
        var exception = Create(endDate: Start);

        Assert.True(exception.IsSingleDay);
        Assert.Equal(1, exception.TotalDays);
        Assert.True(exception.Covers(Start));
    }

    [Fact]
    public void EndDateBeforeStart_IsRejected()
    {
        // A backwards range would make Covers() true for nothing at all, which is
        // an exception that silently does nothing - worse than a rejected input.
        var error = Assert.Throws<DomainException>(() => Create(endDate: Start.AddDays(-1)));

        Assert.Contains("must not be before", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ATimeWindowAppliesToEveryDayOfTheRange()
    {
        var exception = Create(endDate: Start.AddDays(3), from: new TimeOnly(10, 0), to: new TimeOnly(12, 0));

        Assert.False(exception.IsWholeDay);
        Assert.Equal(4, exception.TotalDays);
        Assert.All(Enumerable.Range(0, 4), i => Assert.True(exception.Covers(Start.AddDays(i))));
    }

    [Fact]
    public void HalfOpenTimesAreStillRejectedOnARange() =>
        Assert.Throws<DomainException>(() => AvailabilityException.Create(
            OrganizerId, Start, new TimeOnly(10, 0), null, AvailabilityExceptionType.Meeting, null, Start.AddDays(2)));
}
