using BookingTracker.Application.BookingSessions.Dtos;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.BookingSessions.Commands.StartBookingSession;

public class StartBookingSessionCommandHandler : IRequestHandler<StartBookingSessionCommand, BookingSessionDto>
{
    private readonly IBookingTrackerDbContext _db;
    private readonly IEventBroadcaster _broadcaster;

    public StartBookingSessionCommandHandler(IBookingTrackerDbContext db, IEventBroadcaster broadcaster)
    {
        _db = db;
        _broadcaster = broadcaster;
    }

    public async Task<BookingSessionDto> Handle(StartBookingSessionCommand request, CancellationToken cancellationToken)
    {
        var page = await _db.BookingPages
            .FirstOrDefaultAsync(p => p.Slug == request.BookingPageSlug && p.IsActive, cancellationToken)
            ?? throw new NotFoundException(nameof(BookingPage), request.BookingPageSlug);

        var context = new ClientContext(request.ClientIp, request.UserAgent);
        var (session, @event) = BookingSession.Start(page.Id, context);

        _db.BookingSessions.Add(session);
        _db.BookingSessionEvents.Add(@event);
        await _db.SaveChangesAsync(cancellationToken);

        await _broadcaster.BroadcastEventAsync(@event.ToDto(), cancellationToken);
        await _broadcaster.BroadcastSessionUpdatedAsync(session.ToDto(), cancellationToken);

        return session.ToDto();
    }
}
