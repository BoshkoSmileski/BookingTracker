using BookingTracker.Application.BookingPages.Dtos;
using BookingTracker.Application.Common.Authorization;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.BookingPages.Commands.BookingFormFields.AddBookingFormField;

public class AddBookingFormFieldCommandHandler : IRequestHandler<AddBookingFormFieldCommand, BookingPageDetailDto>
{
    private readonly IBookingTrackerDbContext _db;

    public AddBookingFormFieldCommandHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<BookingPageDetailDto> Handle(AddBookingFormFieldCommand request, CancellationToken cancellationToken)
    {
        await OwnershipGuard.EnsureOrganizerOwnsBookingPageAsync(_db, request.BookingPageId, request.OrganizerId, cancellationToken);

        var page = await _db.BookingPages.FirstAsync(p => p.Id == request.BookingPageId, cancellationToken);
        page.AddFormField(request.Label, request.Type, request.IsRequired);
        await _db.SaveChangesAsync(cancellationToken);

        return page.ToDetailDto();
    }
}
