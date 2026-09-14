using BookingTracker.Application.BookingPages.Dtos;
using BookingTracker.Application.Common.Authorization;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.BookingPages.Commands.UpdateBookingPageLimits;

public class UpdateBookingPageLimitsCommandHandler : IRequestHandler<UpdateBookingPageLimitsCommand, BookingPageDetailDto>
{
    private readonly IBookingTrackerDbContext _db;

    public UpdateBookingPageLimitsCommandHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<BookingPageDetailDto> Handle(UpdateBookingPageLimitsCommand request, CancellationToken cancellationToken)
    {
        await OwnershipGuard.EnsureOrganizerOwnsBookingPageAsync(_db, request.BookingPageId, request.OrganizerId, cancellationToken);

        var page = await _db.BookingPages.FirstAsync(p => p.Id == request.BookingPageId, cancellationToken);
        page.UpdateLimits(request.MinNoticeMinutes, request.MaxBookingWindowDays, request.MaxBookingsPerDay);
        await _db.SaveChangesAsync(cancellationToken);

        return page.ToDetailDto();
    }
}
