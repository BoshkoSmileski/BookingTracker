using BookingTracker.Application.BookingSessions.Dtos;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using BookingTracker.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.BookingSessions.Queries.RebuildBookingSessionState;

public class RebuildBookingSessionStateQueryHandler : IRequestHandler<RebuildBookingSessionStateQuery, BookingSessionDto>
{
    private readonly IBookingTrackerDbContext _db;

    public RebuildBookingSessionStateQueryHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<BookingSessionDto> Handle(RebuildBookingSessionStateQuery request, CancellationToken cancellationToken)
    {
        var events = await _db.BookingSessionEvents
            .AsNoTracking()
            .Where(e => e.SessionId == request.SessionId)
            .OrderBy(e => e.ClientSequenceNumber)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);

        if (events.Count == 0)
            throw new NotFoundException(nameof(BookingSession), request.SessionId);

        var rebuilt = BookingSession.Rebuild(events[0].BookingPageId, events);
        return rebuilt.ToDto();
    }
}
