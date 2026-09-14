using BookingTracker.Application.BookingSessions.Dtos;
using BookingTracker.Application.Common.Authorization;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using BookingTracker.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.BookingSessions.Queries.GetBookingSessionTimeline;

/// <summary>
/// Returns the full, immutable event history for one session, in the order
/// the interactions actually happened on the client - this is what powers
/// the organizer-facing timeline view.
/// </summary>
public class GetBookingSessionTimelineQueryHandler
    : IRequestHandler<GetBookingSessionTimelineQuery, IReadOnlyList<BookingSessionEventDto>>
{
    private readonly IBookingTrackerDbContext _db;

    public GetBookingSessionTimelineQueryHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<IReadOnlyList<BookingSessionEventDto>> Handle(GetBookingSessionTimelineQuery request, CancellationToken cancellationToken)
    {
        // Unconditional, and that is the point: this used to be wrapped in an
        // "if a RequestingOrganizerId was supplied" check, which the anonymous
        // route silently failed. The parameter is required now, so there is no
        // longer a way to reach the event rows without this running first.
        var bookingPageId = await _db.BookingSessions
            .Where(s => s.Id == request.SessionId)
            .Select(s => (Guid?)s.BookingPageId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(BookingSession), request.SessionId);

        await OwnershipGuard.EnsureOrganizerOwnsBookingPageAsync(
            _db, bookingPageId, request.RequestingOrganizerId, cancellationToken);

        var events = await _db.BookingSessionEvents
            .AsNoTracking()
            .Where(e => e.SessionId == request.SessionId)
            .OrderBy(e => e.ClientSequenceNumber)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);

        return events.Select(e => e.ToDto()).ToList();
    }
}
