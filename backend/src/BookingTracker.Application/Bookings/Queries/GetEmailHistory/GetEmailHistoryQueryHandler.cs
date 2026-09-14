using BookingTracker.Application.BookingSessions.Dtos;
using BookingTracker.Application.Common.Authorization;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.Bookings.Queries.GetEmailHistory;

public class GetEmailHistoryQueryHandler : IRequestHandler<GetEmailHistoryQuery, IReadOnlyList<BookingSessionEventDto>>
{
    private readonly IBookingTrackerDbContext _db;

    public GetEmailHistoryQueryHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<IReadOnlyList<BookingSessionEventDto>> Handle(GetEmailHistoryQuery request, CancellationToken cancellationToken)
    {
        var bookingPageId = await _db.BookingSessions
            .Where(s => s.Id == request.SessionId)
            .Select(s => (Guid?)s.BookingPageId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(BookingSession), request.SessionId);

        await OwnershipGuard.EnsureOrganizerOwnsBookingPageAsync(_db, bookingPageId, request.RequestingOrganizerId, cancellationToken);

        var events = await _db.BookingSessionEvents.AsNoTracking()
            .Where(e => e.SessionId == request.SessionId && (e.EventType == BookingEventType.EmailSent || e.EventType == BookingEventType.ReminderSent))
            .OrderBy(e => e.ClientSequenceNumber)
            .ToListAsync(cancellationToken);

        return events.Select(e => e.ToDto()).ToList();
    }
}
