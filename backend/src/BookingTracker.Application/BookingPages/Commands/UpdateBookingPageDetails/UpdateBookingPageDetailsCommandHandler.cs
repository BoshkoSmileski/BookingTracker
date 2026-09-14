using BookingTracker.Application.BookingPages.Dtos;
using BookingTracker.Application.Common.Authorization;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.BookingPages.Commands.UpdateBookingPageDetails;

public class UpdateBookingPageDetailsCommandHandler : IRequestHandler<UpdateBookingPageDetailsCommand, BookingPageDetailDto>
{
    private readonly IBookingTrackerDbContext _db;

    public UpdateBookingPageDetailsCommandHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<BookingPageDetailDto> Handle(UpdateBookingPageDetailsCommand request, CancellationToken cancellationToken)
    {
        await OwnershipGuard.EnsureOrganizerOwnsBookingPageAsync(_db, request.BookingPageId, request.OrganizerId, cancellationToken);

        var page = await _db.BookingPages.FirstAsync(p => p.Id == request.BookingPageId, cancellationToken);
        page.UpdateDetails(request.Title, request.Description);
        await _db.SaveChangesAsync(cancellationToken);

        return page.ToDetailDto();
    }
}
