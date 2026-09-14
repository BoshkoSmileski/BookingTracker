using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.Availability.Commands.DeleteAvailabilityOverride;

public class DeleteAvailabilityOverrideCommandHandler : IRequestHandler<DeleteAvailabilityOverrideCommand>
{
    private readonly IBookingTrackerDbContext _db;

    public DeleteAvailabilityOverrideCommandHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task Handle(DeleteAvailabilityOverrideCommand request, CancellationToken cancellationToken)
    {
        var @override = await _db.AvailabilityOverrides
            .FirstOrDefaultAsync(o => o.Id == request.OverrideId, cancellationToken)
            ?? throw new NotFoundException(nameof(Domain.Entities.AvailabilityOverride), request.OverrideId);

        // Same shape as DeleteAvailabilityExceptionCommandHandler: the id comes
        // from the client, so ownership is re-checked server-side rather than
        // assumed from the route.
        if (@override.OrganizerId != request.OrganizerId)
            throw new ForbiddenException("You do not have access to this availability override.");

        _db.AvailabilityOverrides.Remove(@override);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
