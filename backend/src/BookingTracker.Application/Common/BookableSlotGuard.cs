using BookingTracker.Application.Availability.Queries.GetAvailableSlots;
using BookingTracker.Application.Common.Exceptions;
using MediatR;

namespace BookingTracker.Application.Common;

/// <summary>
/// Refuses a slot the booking page does not actually offer.
///
/// <para>
/// This exists because <see cref="BookingConflictChecker"/> answers a narrower
/// question than its position in the submit path suggests: it asks whether the
/// chosen time <i>collides with another booking</i>, and nothing else. Working
/// hours, blocked dates, date-specific overrides, minimum notice, the booking
/// window, the per-day cap and the page's own active flag were therefore
/// enforced only by <c>GET /slots</c> - which is to say, only by clients that
/// choose to ask. An anonymous caller constructing requests by hand could put a
/// confirmed booking on a closed Saturday, at 03:00, in the past, ten years out,
/// or past a per-day cap, and it would receive a booking reference, a public
/// token, confirmation emails, reminders and a real calendar event. Measured
/// over HTTP, not reasoned about: every one of those returned 200 while
/// <c>GET /slots</c> for the identical date returned an empty array.
/// </para>
///
/// <para>
/// It asks the question by <b>dispatching the existing query</b> rather than
/// re-deriving any rule. That is the no-duplicated-business-logic rule applied to the one place
/// that had quietly grown a second, weaker answer: slot generation lives in
/// <c>SlotGenerationService</c>, its orchestration in
/// <c>GetAvailableSlotsQueryHandler</c>, and this guard inherits every rule
/// either of them gains - including ones added after it was written - without
/// naming a single one of them. The same call the analytics export makes when
/// it composes its report through <see cref="ISender"/> instead of querying.
/// </para>
///
/// <para>
/// <b>Deliberately outside the claim transaction.</b> Slot generation issues
/// several queries and a (fail-open, cached) calendar read, and the Serializable
/// window is deliberately kept down to one gate row and one seek. Nothing is lost by
/// checking here: what is racy about a slot is whether another guest has taken
/// it, and that is re-checked inside the transaction by
/// <see cref="BookingConflictChecker.EnsureSlotIsAvailableAsync"/>. Working
/// hours do not change underneath a request.
/// </para>
///
/// <para>
/// <b>Answers 409, not 400</b>, and with the message the conflict check already
/// uses. A guest who reaches this has almost always been beaten to the slot or
/// left a stale calendar open, and the wizard's recovery path keys on 409 to
/// offer "Choose another time" - so a distinct status here would produce a
/// correct rejection the one screen designed for it never shows.
/// </para>
/// </summary>
public static class BookableSlotGuard
{
    /// <summary>
    /// Throws <see cref="ConflictException"/> unless <paramref name="time"/> is
    /// one of the start times the page is currently offering on
    /// <paramref name="date"/>.
    /// </summary>
    public static async Task EnsureSlotIsOfferedAsync(
        ISender sender,
        Guid bookingPageId,
        DateOnly date,
        TimeOnly time,
        CancellationToken cancellationToken)
    {
        var offered = await sender.Send(new GetAvailableSlotsQuery(bookingPageId, date, date), cancellationToken);

        // Start time, not overlap: a booking occupies the page's own duration
        // from a slot boundary, so "is this one of the offered slots" is the
        // whole question. Comparing loosely would let a caller book at 10:07
        // between two legitimate 10:00/10:30 slots.
        if (offered.Any(slot => slot.LocalDate == date && slot.LocalStartTime == time)) return;

        throw new ConflictException("This time slot is no longer available. Please choose another time.");
    }
}
