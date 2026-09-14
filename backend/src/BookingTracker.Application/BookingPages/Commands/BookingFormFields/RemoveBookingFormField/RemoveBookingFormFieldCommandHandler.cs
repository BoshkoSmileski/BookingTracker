using BookingTracker.Application.BookingPages.Dtos;
using BookingTracker.Application.Common.Authorization;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.BookingPages.Commands.BookingFormFields.RemoveBookingFormField;

public class RemoveBookingFormFieldCommandHandler : IRequestHandler<RemoveBookingFormFieldCommand, BookingPageDetailDto>
{
    private readonly IBookingTrackerDbContext _db;

    public RemoveBookingFormFieldCommandHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<BookingPageDetailDto> Handle(RemoveBookingFormFieldCommand request, CancellationToken cancellationToken)
    {
        await OwnershipGuard.EnsureOrganizerOwnsBookingPageAsync(_db, request.BookingPageId, request.OrganizerId, cancellationToken);

        var page = await _db.BookingPages.FirstAsync(p => p.Id == request.BookingPageId, cancellationToken);
        page.RemoveFormField(request.FieldId);
        await _db.SaveChangesAsync(cancellationToken);

        return page.ToDetailDto();
    }
}
