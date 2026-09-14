using BookingTracker.Domain.Common;
using BookingTracker.Domain.Enums;
using BookingTracker.Domain.Exceptions;

namespace BookingTracker.Domain.Entities;

/// <summary>
/// A one-off override that removes availability over a date range, either for
/// whole days (StartTime/EndTime both null - covers both "blocked period" and
/// "holiday" from the spec, which are functionally identical) or for a
/// sub-range of each day (e.g. a recurring 10:00-12:00 meeting).
///
/// <see cref="Date"/> is the inclusive first day and <see cref="EndDate"/> the
/// inclusive last, so a two-week vacation is ONE row rather than fourteen. A
/// single-day exception simply has the two equal, which is what
/// <see cref="Create"/> produces when no end date is given - so the range is an
/// addition to the original single-date model, not a replacement for it.
///
/// Storing the range rather than expanding it into one row per day is what makes
/// "cancel my vacation" a single delete, and what keeps the organizer's list
/// readable; nothing downstream needs the expansion, because every consumer asks
/// <see cref="Covers"/> about one date at a time.
/// </summary>
public class AvailabilityException : Entity<Guid>
{
    /// <summary>Inclusive first day of the exception.</summary>
    public Guid OrganizerId { get; private set; }
    public DateOnly Date { get; private set; }

    /// <summary>Inclusive last day. Equal to <see cref="Date"/> for a single-day exception.</summary>
    public DateOnly EndDate { get; private set; }

    /// <summary>When set, the exception blocks only this window - on every day of the range.</summary>
    public TimeOnly? StartTime { get; private set; }
    public TimeOnly? EndTime { get; private set; }
    public AvailabilityExceptionType Type { get; private set; }
    public string? Reason { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public bool IsWholeDay => StartTime is null;

    public bool IsSingleDay => Date == EndDate;

    /// <summary>Number of days covered, inclusive of both ends - 1 for a single-day exception.</summary>
    public int TotalDays => EndDate.DayNumber - Date.DayNumber + 1;

    /// <summary>
    /// Whether this exception applies on <paramref name="date"/>. The single
    /// place that knows the range is inclusive at both ends, so slot generation
    /// and any future consumer cannot disagree about the boundary days.
    /// </summary>
    public bool Covers(DateOnly date) => date >= Date && date <= EndDate;

    private AvailabilityException() { }

    /// <param name="endDate">Inclusive last day. Null means a single-day exception on <paramref name="date"/>.</param>
    public static AvailabilityException Create(
        Guid organizerId,
        DateOnly date,
        TimeOnly? startTime,
        TimeOnly? endTime,
        AvailabilityExceptionType type,
        string? reason,
        DateOnly? endDate = null)
    {
        var lastDay = endDate ?? date;

        if (lastDay < date)
            throw new DomainException($"Exception end date ({lastDay}) must not be before its start date ({date}).");

        if (startTime.HasValue != endTime.HasValue)
            throw new DomainException("StartTime and EndTime must both be set, or both be null for a whole-day exception.");

        if (startTime.HasValue && startTime.Value >= endTime!.Value)
            throw new DomainException($"Exception start ({startTime}) must be before end ({endTime}).");

        return new AvailabilityException
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizerId,
            Date = date,
            EndDate = lastDay,
            StartTime = startTime,
            EndTime = endTime,
            Type = type,
            Reason = reason,
            CreatedAt = DateTime.UtcNow
        };
    }
}
