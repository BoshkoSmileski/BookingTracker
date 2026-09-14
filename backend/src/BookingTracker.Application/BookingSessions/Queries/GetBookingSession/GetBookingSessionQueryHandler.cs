using BookingTracker.Application.BookingSessions.Dtos;
using BookingTracker.Application.Common.Authorization;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using BookingTracker.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.BookingSessions.Queries.GetBookingSession;

public class GetBookingSessionQueryHandler : IRequestHandler<GetBookingSessionQuery, BookingSessionDto>
{
    private readonly IBookingTrackerDbContext _db;

    public GetBookingSessionQueryHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<BookingSessionDto> Handle(GetBookingSessionQuery request, CancellationToken cancellationToken)
    {
        var session = await _db.BookingSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.SessionId, cancellationToken)
            ?? throw new NotFoundException(nameof(BookingSession), request.SessionId);

        if (request.RequestingOrganizerId is { } organizerId)
        {
            await OwnershipGuard.EnsureOrganizerOwnsBookingPageAsync(_db, session.BookingPageId, organizerId, cancellationToken);
        }

        return session.ToDto();
    }
}
