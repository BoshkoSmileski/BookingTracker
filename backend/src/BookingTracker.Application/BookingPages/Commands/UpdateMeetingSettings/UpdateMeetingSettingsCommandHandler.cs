using BookingTracker.Application.BookingPages.Dtos;
using BookingTracker.Application.Common.Authorization;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.BookingPages.Commands.UpdateMeetingSettings;

public class UpdateMeetingSettingsCommandHandler : IRequestHandler<UpdateMeetingSettingsCommand, BookingPageDetailDto>
{
    private readonly IBookingTrackerDbContext _db;

    public UpdateMeetingSettingsCommandHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<BookingPageDetailDto> Handle(UpdateMeetingSettingsCommand request, CancellationToken cancellationToken)
    {
        await OwnershipGuard.EnsureOrganizerOwnsBookingPageAsync(_db, request.BookingPageId, request.OrganizerId, cancellationToken);

        var page = await _db.BookingPages.FirstAsync(p => p.Id == request.BookingPageId, cancellationToken);
        // Only future bookings are affected: nothing here touches the sessions
        // that already exist, whose meeting (or lack of one) was settled when
        // they were confirmed. Deliberately unlike the reminder settings resync,
        // because a meeting link is not a schedule that can be regenerated -
        // it is a URL guests have already been sent.
        page.UpdateMeetingSettings(request.MeetingProvider);
        await _db.SaveChangesAsync(cancellationToken);

        return page.ToDetailDto();
    }
}
