using BookingTracker.Application.BookingPages.Dtos;
using BookingTracker.Application.Common.Authorization;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.BookingPages.Commands.BookingInstructions.RemoveBookingInstruction;

public class RemoveBookingInstructionCommandHandler : IRequestHandler<RemoveBookingInstructionCommand, BookingPageDetailDto>
{
    private readonly IBookingTrackerDbContext _db;

    public RemoveBookingInstructionCommandHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<BookingPageDetailDto> Handle(RemoveBookingInstructionCommand request, CancellationToken cancellationToken)
    {
        await OwnershipGuard.EnsureOrganizerOwnsBookingPageAsync(_db, request.BookingPageId, request.OrganizerId, cancellationToken);

        var page = await _db.BookingPages.FirstAsync(p => p.Id == request.BookingPageId, cancellationToken);
        page.RemoveQuestion(request.InstructionId);
        await _db.SaveChangesAsync(cancellationToken);

        return page.ToDetailDto();
    }
}
