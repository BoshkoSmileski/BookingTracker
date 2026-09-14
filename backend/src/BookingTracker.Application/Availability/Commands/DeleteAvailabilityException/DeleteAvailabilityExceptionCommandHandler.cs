using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.Availability.Commands.DeleteAvailabilityException;

public class DeleteAvailabilityExceptionCommandHandler : IRequestHandler<DeleteAvailabilityExceptionCommand>
{
    private readonly IBookingTrackerDbContext _db;

    public DeleteAvailabilityExceptionCommandHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task Handle(DeleteAvailabilityExceptionCommand request, CancellationToken cancellationToken)
    {
        var exception = await _db.AvailabilityExceptions
            .FirstOrDefaultAsync(e => e.Id == request.ExceptionId, cancellationToken)
            ?? throw new NotFoundException(nameof(Domain.Entities.AvailabilityException), request.ExceptionId);

        if (exception.OrganizerId != request.OrganizerId)
            throw new ForbiddenException("You do not have access to this availability exception.");

        _db.AvailabilityExceptions.Remove(exception);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
