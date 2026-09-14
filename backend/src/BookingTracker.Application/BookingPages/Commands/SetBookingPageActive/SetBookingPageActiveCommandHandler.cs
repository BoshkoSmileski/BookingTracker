using BookingTracker.Application.BookingPages.Dtos;
using BookingTracker.Application.Common.Authorization;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.BookingPages.Commands.SetBookingPageActive;

public class SetBookingPageActiveCommandHandler : IRequestHandler<SetBookingPageActiveCommand, BookingPageDetailDto>
{
    private readonly IBookingTrackerDbContext _db;

    public SetBookingPageActiveCommandHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<BookingPageDetailDto> Handle(SetBookingPageActiveCommand request, CancellationToken cancellationToken)
    {
        await OwnershipGuard.EnsureOrganizerOwnsBookingPageAsync(_db, request.BookingPageId, request.OrganizerId, cancellationToken);

        var page = await _db.BookingPages.FirstAsync(p => p.Id == request.BookingPageId, cancellationToken);
        if (request.IsActive) page.Activate(); else page.Deactivate();
        await _db.SaveChangesAsync(cancellationToken);

        return page.ToDetailDto();
    }
}
