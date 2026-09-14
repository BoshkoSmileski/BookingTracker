using BookingTracker.Domain.Common;
using BookingTracker.Domain.Exceptions;
using BookingTracker.Domain.ValueObjects;

namespace BookingTracker.Domain.Entities;

/// <summary>
/// Date-specific opening hours: what this organizer's day looks like on ONE
/// calendar date, replacing the weekly schedule for that date entirely.
///
/// This is the availability model's **second producer of open time**, and the
/// only other one - <see cref="WorkingDay"/> is the first. Everything else in
/// the system (<see cref="AvailabilityException"/>, existing bookings, Google
/// busy intervals, booking limits) only ever *subtracts*, which is exactly why
/// an override could not be expressed by reusing any of them: a disabled
/// weekday is skipped before exceptions are ever consulted, so nothing
/// subtractive can make Saturday bookable.
///
/// DELIBERATELY NOT the same entity as AvailabilityException. That type means
/// "remove availability" and its Covers() is asked by slot generation in that
/// sense; giving it an inverted mode would make one predicate mean two opposite
/// things and change the meaning of every existing consumer. They also compose
/// rather than compete - see the precedence rules below.
///
/// PRECEDENCE, in the order slot generation applies it:
///   1. An override for the date REPLACES the weekly schedule for that date.
///      Weekly hours are not consulted at all - not merged, not intersected.
///   2. No ranges means CLOSED ("Christmas Eve: closed"), which is why
///      <see cref="IsClosed"/> is derived from the collection rather than
///      stored as a second, independently-settable flag that could disagree
///      with it.
///   3. Blocked dates (AvailabilityException) are still subtracted afterwards,
///      from override hours exactly as from weekly hours. An override opens a
///      day; it does not un-block one. A whole-day block therefore still wins.
///   4. Existing bookings, buffers, minimum notice, the booking window, the
///      per-day cap and Google busy intervals all apply unchanged.
///
/// Organizer-scoped, like WorkingSchedule and AvailabilityException: an
/// override describes when this person is available, so it applies to every
/// booking page they own. At most one per date - "the hours for 15 August" is
/// one fact, and two rows for one date would need a tie-break rule that no
/// user could predict.
/// </summary>
public class AvailabilityOverride : Entity<Guid>
{
    /// <summary>
    /// Enough to model a real working day (morning, afternoon, evening) and few
    /// enough that the editor stays a form rather than a spreadsheet. Matches
    /// the spirit of BookingPage.MaxFormFields.
    /// </summary>
    public const int MaxRanges = 6;

    private readonly List<TimeRange> _ranges = [];

    public Guid OrganizerId { get; private set; }

    /// <summary>The single calendar date these hours apply to. Organizer-local wall clock, never a UTC instant.</summary>
    public DateOnly Date { get; private set; }

    public string? Note { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    /// <summary>The day's opening hours, ordered by start. Empty means the day is closed.</summary>
    public IReadOnlyList<TimeRange> Ranges => _ranges.AsReadOnly();

    /// <summary>
    /// Derived, never stored: "closed" IS "no open hours". A separate boolean
    /// column could contradict the ranges beside it, and there would be no
    /// answer to which one slot generation should believe.
    /// </summary>
    public bool IsClosed => _ranges.Count == 0;

    private AvailabilityOverride() { }

    public static AvailabilityOverride Create(Guid organizerId, DateOnly date, IEnumerable<TimeRange> ranges, string? note = null)
    {
        var result = new AvailabilityOverride
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizerId,
            Date = date,
            Note = note,
            CreatedAt = DateTime.UtcNow
        };

        result.ReplaceRanges(ranges);
        return result;
    }

    /// <summary>
    /// Replaces the whole day's hours in one call rather than exposing
    /// add/remove-one-range methods. Editing a day means stating what it looks
    /// like now, and a whole-day replace cannot leave the row in a state the
    /// organizer did not ask for - the same reasoning as
    /// SaveWorkingScheduleCommand's idempotent upsert of the entire week.
    /// </summary>
    public void Update(IEnumerable<TimeRange> ranges, string? note)
    {
        ReplaceRanges(ranges);
        Note = note;
        UpdatedAt = DateTime.UtcNow;
    }

    private void ReplaceRanges(IEnumerable<TimeRange> ranges)
    {
        var ordered = ranges.OrderBy(r => r.Start).ToList();

        if (ordered.Count > MaxRanges)
            throw new DomainException($"A date-specific schedule cannot have more than {MaxRanges} time ranges.");

        for (var i = 1; i < ordered.Count; i++)
        {
            // Overlapping open hours would silently produce duplicate slots for
            // the same minute, so they are rejected rather than merged - the
            // same call WorkingDay.AddInterval makes for the weekly schedule.
            if (ordered[i - 1].Overlaps(ordered[i]))
                throw new DomainException($"Time ranges {ordered[i - 1].Start}-{ordered[i - 1].End} and {ordered[i].Start}-{ordered[i].End} overlap.");
        }

        _ranges.Clear();
        // Cloned for the same reason WorkingDay.AddInterval clones: EF Core
        // identifies owned instances by reference, so one TimeRange instance
        // must never be shared between two owners.
        foreach (var range in ordered) _ranges.Add(range with { });
    }
}
