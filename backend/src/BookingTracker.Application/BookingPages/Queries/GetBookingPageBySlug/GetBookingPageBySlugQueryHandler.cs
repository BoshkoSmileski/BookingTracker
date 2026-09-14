using BookingTracker.Application.BookingPages.Dtos;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using BookingTracker.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.BookingPages.Queries.GetBookingPageBySlug;

public class GetBookingPageBySlugQueryHandler : IRequestHandler<GetBookingPageBySlugQuery, BookingPageDto>
{
    private readonly IBookingTrackerDbContext _db;

    public GetBookingPageBySlugQueryHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<BookingPageDto> Handle(GetBookingPageBySlugQuery request, CancellationToken cancellationToken)
    {
        var page = await _db.BookingPages
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Slug == request.Slug && p.IsActive, cancellationToken)
            ?? throw new NotFoundException(nameof(BookingPage), request.Slug);

        var organizerName = await _db.Organizers
            .AsNoTracking()
            .Where(o => o.Id == page.OrganizerId)
            .Select(o => o.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "Organizer";

        // Whose clock every time on this page is on. Every value the wizard
        // shows a visitor - slot times, the selected time, the confirmation -
        // is organizer-local wall clock, so the page has to be able to say so.
        // "UTC" when the organizer has never configured a schedule, in which
        // case GetAvailableSlots returns nothing anyway.
        var timeZoneId = await _db.WorkingSchedules
            .AsNoTracking()
            .Where(s => s.OrganizerId == page.OrganizerId)
            .Select(s => s.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken) ?? "UTC";

        return page.ToDto(organizerName, timeZoneId);
    }
}
