using BookingTracker.Domain.Entities;
using BookingTracker.Domain.ValueObjects;

namespace BookingTracker.Domain.Services;

/// <summary>
/// Pure computation, no I/O: turns working hours + exceptions + already-booked
/// windows into a list of bookable slots. Never persists anything - the whole
/// point of this feature is that slots are derived on every request instead of
/// materialized as rows. All interval arithmetic is done in "minutes since
/// midnight" integers rather than TimeOnly directly, to sidestep TimeOnly's
/// day-wraparound semantics entirely.
///
/// The day's OPEN hours come from exactly one of two producers, resolved once
/// per date by <see cref="ResolveOpenRanges"/>: a date-specific
/// <see cref="AvailabilityOverride"/> if one exists, otherwise the weekly
/// <see cref="WorkingDay"/>. Everything after that point - exceptions,
/// bookings, buffers, notice, the clock - is subtractive and runs identically
/// whichever producer supplied the hours, which is what keeps overrides from
/// being a second copy of the slot loop.
/// </summary>
public static class SlotGenerationService
{
    public const int DefaultSlotStepMinutes = 15;
    private const int MinutesPerDay = 24 * 60;

    /// <param name="overrides">
    /// Date-specific opening hours. An override REPLACES the weekly schedule for
    /// its date (see AvailabilityOverride for the full precedence rules); an
    /// override with no ranges closes the day. Pass an empty list for
    /// "weekly schedule only", which is what every caller did before
    /// date-specific hours existed.
    /// </param>
    public static IReadOnlyList<AvailableSlot> GenerateSlots(
        IReadOnlyList<WorkingDay> workingDays,
        IReadOnlyList<AvailabilityException> exceptions,
        IReadOnlyList<OccupiedInterval> existingBookings,
        TimeZoneInfo organizerTimeZone,
        int serviceDurationMinutes,
        int bufferBeforeMinutes,
        int bufferAfterMinutes,
        DateOnly fromDate,
        DateOnly toDate,
        DateTime nowUtc,
        int slotStepMinutes = DefaultSlotStepMinutes,
        int minNoticeMinutes = 0,
        IReadOnlyList<AvailabilityOverride>? overrides = null)
    {
        if (toDate < fromDate || serviceDurationMinutes <= 0) return [];

        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, organizerTimeZone);
        var todayLocal = DateOnly.FromDateTime(nowLocal);
        var nowMinutesLocal = ToMinutes(TimeOnly.FromDateTime(nowLocal));
        var earliestBookableUtc = nowUtc.AddMinutes(minNoticeMinutes);

        var slots = new List<AvailableSlot>();

        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            // The one place the two producers are reconciled. Everything below
            // this line is producer-agnostic.
            var openRanges = ResolveOpenRanges(date, workingDays, overrides);
            if (openRanges.Count == 0) continue;

            // Covers() rather than a date comparison here: an exception spans an
            // inclusive range (a vacation is one row, not one per day), and the
            // entity is the only place that knows where its boundaries are.
            //
            // Applied to override hours exactly as to weekly hours: an override
            // says when the day is OPEN, it does not un-block a blocked date, so
            // a whole-day block still closes a day an override opened.
            var dayExceptions = exceptions.Where(e => e.Covers(date)).ToList();
            if (dayExceptions.Any(e => e.IsWholeDay)) continue;

            var blockRanges = dayExceptions
                .Select(e => (Start: ToMinutes(e.StartTime!.Value), End: ToMinutes(e.EndTime!.Value)))
                .ToList();

            foreach (var interval in openRanges)
            {
                var freeRanges = Subtract((ToMinutes(interval.Start), ToMinutes(interval.End)), blockRanges);

                foreach (var free in freeRanges)
                {
                    for (var slotStart = free.Start; slotStart + serviceDurationMinutes <= free.End; slotStart += slotStepMinutes)
                    {
                        if (date == todayLocal && slotStart <= nowMinutesLocal) continue;

                        var slotEnd = slotStart + serviceDurationMinutes;
                        var occupiedStart = Math.Max(0, slotStart - bufferBeforeMinutes);
                        var occupiedEnd = Math.Min(MinutesPerDay, slotEnd + bufferAfterMinutes);

                        var hasConflict = existingBookings.Any(b =>
                            b.Overlaps(date, FromMinutes(occupiedStart), FromMinutes(occupiedEnd)));
                        if (hasConflict) continue;

                        var slot = ToSlot(date, slotStart, slotEnd, organizerTimeZone);
                        if (slot.StartUtc < earliestBookableUtc) continue;
                        slots.Add(slot);
                    }
                }
            }
        }

        return slots.OrderBy(s => s.Date).ThenBy(s => s.StartTime).ToList();
    }

    /// <summary>
    /// The day's open hours, from whichever producer owns this date.
    ///
    /// A date-specific override REPLACES the weekly schedule outright - not
    /// merged, not intersected - because "on 15 August I work 13:00-17:00" is a
    /// statement about the whole day, and any blending rule would make the
    /// organizer's stated hours something they have to derive rather than read.
    /// An override with no ranges is a closed day, which is what lets an
    /// override close a normally-open weekday; the absence of an override is
    /// what falls through to the weekly schedule.
    ///
    /// This is also the only reason overrides can OPEN a normally-closed day:
    /// the weekly lookup is no longer the gate that decides whether the date is
    /// considered at all.
    /// </summary>
    private static IReadOnlyList<TimeRange> ResolveOpenRanges(
        DateOnly date,
        IReadOnlyList<WorkingDay> workingDays,
        IReadOnlyList<AvailabilityOverride>? overrides)
    {
        var dayOverride = overrides?.FirstOrDefault(o => o.Date == date);
        if (dayOverride is not null) return dayOverride.Ranges;

        var workingDay = workingDays.FirstOrDefault(d => d.IsEnabled && d.DayOfWeek == date.DayOfWeek);
        return workingDay?.Intervals ?? [];
    }

    /// <summary>Subtracts a set of blocked minute-ranges from one base minute-range, returning what's left.</summary>
    private static List<(int Start, int End)> Subtract((int Start, int End) baseRange, List<(int Start, int End)> blocks)
    {
        var result = new List<(int Start, int End)> { baseRange };

        foreach (var block in blocks.OrderBy(b => b.Start))
        {
            var next = new List<(int Start, int End)>();
            foreach (var r in result)
            {
                if (block.End <= r.Start || block.Start >= r.End)
                {
                    next.Add(r);
                    continue;
                }

                if (block.Start > r.Start) next.Add((r.Start, Math.Min(block.Start, r.End)));
                if (block.End < r.End) next.Add((Math.Max(block.End, r.Start), r.End));
            }
            result = next;
        }

        return result;
    }

    private static AvailableSlot ToSlot(DateOnly date, int startMinutes, int endMinutes, TimeZoneInfo timeZone)
    {
        var startTime = FromMinutes(startMinutes);
        var endTime = FromMinutes(endMinutes);

        var startLocal = DateTime.SpecifyKind(date.ToDateTime(startTime), DateTimeKind.Unspecified);
        var endLocal = DateTime.SpecifyKind(date.ToDateTime(endTime), DateTimeKind.Unspecified);

        return new AvailableSlot(
            date,
            startTime,
            endTime,
            TimeZoneInfo.ConvertTimeToUtc(startLocal, timeZone),
            TimeZoneInfo.ConvertTimeToUtc(endLocal, timeZone));
    }

    private static int ToMinutes(TimeOnly t) => t.Hour * 60 + t.Minute;

    private static TimeOnly FromMinutes(int minutes) => new(minutes / 60 % 24, minutes % 60);
}
