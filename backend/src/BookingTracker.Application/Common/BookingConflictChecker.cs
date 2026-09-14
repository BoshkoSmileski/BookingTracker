using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.Common;

/// <summary>
/// The transactional "is this slot still free" check, shared by every command
/// that claims a slot (SubmitBookingSessionCommandHandler, RescheduleBookingCommandHandler)
/// so the double-booking-prevention logic exists exactly once. Callers are expected
/// to run this inside IBookingTrackerDbContext.ExecuteInTransactionAsync for the
/// race-condition-safety guarantee to actually hold.
/// </summary>
public static class BookingConflictChecker
{
    /// <summary>
    /// Marks the organizer read in <see cref="AcquireClaimGateAsync"/> as the
    /// point where concurrent claims take turns, so the storage layer can give
    /// it an UPDATE lock rather than a shared one.
    ///
    /// Tagged rather than expressed here because the mechanism is a SQL Server
    /// locking hint and Application may not contain provider SQL: the tag is the
    /// intent, and BookingTracker.Infrastructure's ClaimLockInterceptor is the
    /// implementation. A provider that has no such hint ignores the tag
    /// entirely and keeps whatever concurrency behaviour it already had, which
    /// is what lets the InMemory-backed suites run this exact code path.
    /// </summary>
    public const string ClaimGateTag = "claim-gate: serialize slot claims for one organizer";

    /// <summary>
    /// The whole double-booking-prevention protocol, in the one place that owns
    /// it: check inside a serializable transaction, claim, and translate a lost
    /// race into this application's own answer.
    ///
    /// The last part is why this exists rather than each handler opening its own
    /// transaction. Serializable isolation is what stops two racing claims both
    /// succeeding, but SQL Server enforces it by choosing one of them as a
    /// deadlock victim - so the loser's request ends in a provider exception
    /// rather than in the ConflictException the check would have produced a
    /// moment later. Measured, not theorised: two guests submitting the same
    /// slot together produced one booking and one HTTP 500.
    ///
    /// So when the transaction is lost to contention, the question it was in the
    /// middle of asking is simply asked again, outside the transaction: is that
    /// slot taken now? If it is, the guest gets the 409 and the "choose another
    /// time" recovery that has always been the intended contract. The re-read
    /// blocks on the winner's uncommitted row under read-committed, so it sees
    /// the committed outcome rather than a half-finished one.
    ///
    /// If the slot is NOT taken, the contention was about something else and the
    /// original failure is rethrown untouched. A lost race is never reported as
    /// a slot conflict on the strength of having been a lost race.
    /// </summary>
    /// <param name="date">Null when the session has not chosen a slot yet - there is then nothing to conflict with, and nothing to re-check.</param>
    public static async Task ClaimSlotAsync(
        IBookingTrackerDbContext db,
        Guid sessionIdToExclude,
        Guid bookingPageId,
        DateOnly? date,
        TimeOnly? time,
        Func<Task> claim,
        CancellationToken cancellationToken)
    {
        try
        {
            await db.ExecuteInTransactionAsync(async () =>
            {
                if (date is { } d && time is { } t)
                {
                    await AcquireClaimGateAsync(db, bookingPageId, cancellationToken);
                    await EnsureSlotIsAvailableAsync(db, sessionIdToExclude, bookingPageId, d, t, cancellationToken);
                }

                await claim();
            }, cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            if (date is { } d && time is { } t)
            {
                await EnsureSlotIsAvailableAsync(db, sessionIdToExclude, bookingPageId, d, t, cancellationToken);
            }

            throw;
        }
    }

    /// <summary>
    /// Takes the claim's turn for this booking page's ORGANIZER, before the
    /// conflict check reads anything, so that two guests claiming against the
    /// same calendar are serialized rather than left to collide.
    ///
    /// Why a separate, deliberately trivial read exists at all: the conflict
    /// check's own read cannot be the serialization point. Locking the range it
    /// scans is data-dependent - the set of keys a seek must lock changes the
    /// moment a booking appears in that range - so waiters resume wanting a
    /// different key than the one they queued on, and at eight or more racers
    /// they deadlock over the ordering. Measured, not theorised: an UPDATE lock
    /// on the conflict read fixed two and four racers and produced 16-20
    /// deadlocks and 8-10 HTTP 500s at sixteen, with SQL Server's own deadlock
    /// graph showing three readers cycling over two keys of one index.
    ///
    /// An organizer row is the opposite of data-dependent. There is exactly one,
    /// it always exists, it is found by a primary-key seek that the optimizer
    /// has no alternative to, and it is taken FIRST - so there is only ever one
    /// lock and no order for anyone to disagree about. It is also exactly the
    /// scope of the rule being enforced: double-booking is organizer-wide (the
    /// conflict query joins through BookingPages on OrganizerId), so one
    /// organizer's guests wait for each other and never for anybody else's.
    /// That is the property the covering index bought and this must not spend.
    ///
    /// The row is only ever read, never written, by this or any other flow -
    /// registration inserts it and nothing updates it - so an update lock here
    /// is never converted, and ordinary readers (which take S) are unaffected.
    ///
    /// Nothing is done with the result. The statement exists for its lock.
    /// </summary>
    private static Task AcquireClaimGateAsync(
        IBookingTrackerDbContext db, Guid bookingPageId, CancellationToken cancellationToken)
    {
        return db.Organizers
            .Where(o => db.BookingPages.Any(p => p.Id == bookingPageId && p.OrganizerId == o.Id))
            .Select(o => o.Id)
            .TagWith(ClaimGateTag)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public static async Task EnsureSlotIsAvailableAsync(
        IBookingTrackerDbContext db,
        Guid sessionIdToExclude,
        Guid bookingPageId,
        DateOnly date,
        TimeOnly time,
        CancellationToken cancellationToken)
    {
        var page = await db.BookingPages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == bookingPageId, cancellationToken)
            ?? throw new NotFoundException(nameof(BookingPage), bookingPageId);

        var candidate = OccupiedInterval.FromBookingWindow(date, time, page.DurationMinutes, page.BufferBeforeMinutes, page.BufferAfterMinutes);

        var sameDayBookings = await (
            from other in db.BookingSessions.AsNoTracking()
            join otherPage in db.BookingPages.AsNoTracking() on other.BookingPageId equals otherPage.Id
            where otherPage.OrganizerId == page.OrganizerId
                && other.Id != sessionIdToExclude
                && other.Status == BookingSessionStatus.Submitted
                && other.SelectedDate == date
            select new { Time = other.SelectedTime!.Value, otherPage.DurationMinutes, otherPage.BufferBeforeMinutes, otherPage.BufferAfterMinutes }
        ).ToListAsync(cancellationToken);

        var hasConflict = sameDayBookings
            .Select(b => OccupiedInterval.FromBookingWindow(date, b.Time, b.DurationMinutes, b.BufferBeforeMinutes, b.BufferAfterMinutes))
            .Any(occupied => occupied.Overlaps(candidate.Date, candidate.Start, candidate.End));

        if (hasConflict)
            throw new ConflictException("This time slot is no longer available. Please choose another time.");
    }
}
