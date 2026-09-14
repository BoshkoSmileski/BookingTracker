using BookingTracker.Application.Common.Authorization;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.BookingPages.Commands.DeleteBookingPage;

public class DeleteBookingPageCommandHandler : IRequestHandler<DeleteBookingPageCommand>
{
    private readonly IBookingTrackerDbContext _db;

    public DeleteBookingPageCommandHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task Handle(DeleteBookingPageCommand request, CancellationToken cancellationToken)
    {
        await OwnershipGuard.EnsureOrganizerOwnsBookingPageAsync(_db, request.BookingPageId, request.OrganizerId, cancellationToken);

        var hasConfirmedBookings = await _db.BookingSessions.AsNoTracking()
            .AnyAsync(s => s.BookingPageId == request.BookingPageId && s.Status == BookingSessionStatus.Submitted, cancellationToken);
        if (hasConfirmedBookings)
            throw new ConflictException("This booking page has confirmed bookings and can't be deleted. Disable it instead.");

        var page = await _db.BookingPages.FirstAsync(p => p.Id == request.BookingPageId, cancellationToken);
        _db.BookingPages.Remove(page);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
