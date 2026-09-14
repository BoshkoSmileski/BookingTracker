using BookingTracker.Application.BookingPages.Dtos;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using BookingTracker.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.Availability.Commands.UpdateBookingPageSchedulingSettings;

public class UpdateBookingPageSchedulingSettingsCommandHandler
    : IRequestHandler<UpdateBookingPageSchedulingSettingsCommand, BookingPageDto>
{
    private readonly IBookingTrackerDbContext _db;

    public UpdateBookingPageSchedulingSettingsCommandHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<BookingPageDto> Handle(UpdateBookingPageSchedulingSettingsCommand request, CancellationToken cancellationToken)
    {
        var page = await _db.BookingPages.FirstOrDefaultAsync(p => p.Id == request.BookingPageId, cancellationToken)
            ?? throw new NotFoundException(nameof(BookingPage), request.BookingPageId);

        if (page.OrganizerId != request.OrganizerId)
            throw new ForbiddenException("You do not have access to this booking page.");

        page.UpdateSchedulingSettings(request.DurationMinutes, request.BufferBeforeMinutes, request.BufferAfterMinutes);
        await _db.SaveChangesAsync(cancellationToken);

        var organizerName = await _db.Organizers
            .AsNoTracking()
            .Where(o => o.Id == request.OrganizerId)
            .Select(o => o.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "Organizer";

        var timeZoneId = await _db.WorkingSchedules
            .AsNoTracking()
            .Where(s => s.OrganizerId == request.OrganizerId)
            .Select(s => s.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken) ?? "UTC";

        return page.ToDto(organizerName, timeZoneId);
    }
}
