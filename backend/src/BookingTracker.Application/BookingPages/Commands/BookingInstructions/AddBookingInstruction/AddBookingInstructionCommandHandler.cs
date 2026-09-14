using BookingTracker.Application.BookingPages.Dtos;
using BookingTracker.Application.Common.Authorization;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.BookingPages.Commands.BookingInstructions.AddBookingInstruction;

public class AddBookingInstructionCommandHandler : IRequestHandler<AddBookingInstructionCommand, BookingPageDetailDto>
{
    private readonly IBookingTrackerDbContext _db;

    public AddBookingInstructionCommandHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<BookingPageDetailDto> Handle(AddBookingInstructionCommand request, CancellationToken cancellationToken)
    {
        await OwnershipGuard.EnsureOrganizerOwnsBookingPageAsync(_db, request.BookingPageId, request.OrganizerId, cancellationToken);

        var page = await _db.BookingPages.FirstAsync(p => p.Id == request.BookingPageId, cancellationToken);
        // AddQuestion is the Domain name for the same thing - see the command.
        page.AddQuestion(request.Text, page.Questions.Count);
        await _db.SaveChangesAsync(cancellationToken);

        return page.ToDetailDto();
    }
}
