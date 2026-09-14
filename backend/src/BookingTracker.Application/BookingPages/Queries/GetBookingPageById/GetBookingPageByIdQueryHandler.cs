using BookingTracker.Application.BookingPages.Dtos;
using BookingTracker.Application.Common.Authorization;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.BookingPages.Queries.GetBookingPageById;

public class GetBookingPageByIdQueryHandler : IRequestHandler<GetBookingPageByIdQuery, BookingPageDetailDto>
{
    private readonly IBookingTrackerDbContext _db;

    public GetBookingPageByIdQueryHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<BookingPageDetailDto> Handle(GetBookingPageByIdQuery request, CancellationToken cancellationToken)
    {
        await OwnershipGuard.EnsureOrganizerOwnsBookingPageAsync(_db, request.BookingPageId, request.OrganizerId, cancellationToken);

        var page = await _db.BookingPages
            .AsNoTracking()
            .FirstAsync(p => p.Id == request.BookingPageId, cancellationToken);

        return page.ToDetailDto();
    }
}
