namespace BookingTracker.Domain.ValueObjects;

/// <summary>
/// A date+time span that is unavailable for a new booking - either an existing
/// booking (already expanded by its own buffer) or a partial-day availability
/// exception. Deliberately minimal: SlotGenerationService only needs to know
/// "this window is taken", not why.
/// </summary>
public readonly record struct OccupiedInterval(DateOnly Date, TimeOnly Start, TimeOnly End)
{
    private const int MinutesPerDay = 24 * 60;

    public bool Overlaps(DateOnly date, TimeOnly start, TimeOnly end)
        => Date == date && Start < end && start < End;

    /// <summary>
    /// Builds the occupied window for a booking that starts at <paramref name="start"/>
    /// and runs for <paramref name="durationMinutes"/>, expanded by its buffers. Buffer
    /// expansion is clamped to the same calendar day rather than spilling into the
    /// next/previous day - consistent with how SlotGenerationService treats working-hour
    /// boundaries, and the reason all "minutes since midnight" arithmetic in this codebase
    /// goes through here instead of TimeOnly.Add (which wraps around midnight silently).
    /// </summary>
    public static OccupiedInterval FromBookingWindow(
        DateOnly date, TimeOnly start, int durationMinutes, int bufferBeforeMinutes, int bufferAfterMinutes)
    {
        var startMinutes = Math.Max(0, ToMinutes(start) - bufferBeforeMinutes);
        var endMinutes = Math.Min(MinutesPerDay, ToMinutes(start) + durationMinutes + bufferAfterMinutes);
        return new OccupiedInterval(date, FromMinutes(startMinutes), FromMinutes(endMinutes));
    }

    private static int ToMinutes(TimeOnly t) => t.Hour * 60 + t.Minute;

    private static TimeOnly FromMinutes(int minutes) => new(minutes / 60 % 24, minutes % 60);
}
