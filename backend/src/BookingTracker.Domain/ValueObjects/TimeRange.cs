using BookingTracker.Domain.Exceptions;

namespace BookingTracker.Domain.ValueObjects;

/// <summary>
/// A half-open [Start, End) interval within a single day.
///
/// NOTE: EF Core 8's OwnsMany does not support struct-typed owned collections
/// (that lands in EF 9's complex-type collections), so this has to stay a
/// reference type to be mapped as WorkingDay's owned Intervals collection.
/// That means the same footgun this project already hit once with
/// ClientContext applies here too: EF Core's owned-type tracker identifies
/// instances by reference, so the SAME TimeRange instance must never be
/// reused across two different owners (e.g. two different WorkingDay rows) -
/// always create a fresh instance (or `range with { }`) per owner.
/// </summary>
public sealed record TimeRange
{
    public TimeOnly Start { get; }
    public TimeOnly End { get; }

    private TimeRange(TimeOnly start, TimeOnly end)
    {
        Start = start;
        End = end;
    }

    public static TimeRange Create(TimeOnly start, TimeOnly end)
    {
        if (start >= end)
            throw new DomainException($"Time range start ({start}) must be before end ({end}).");

        return new TimeRange(start, end);
    }

    public bool Overlaps(TimeRange other) => Start < other.End && other.Start < End;

    public bool Contains(TimeRange other) => Start <= other.Start && other.End <= End;

    public TimeSpan Duration => End - Start;
}
