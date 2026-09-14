using BookingTracker.Application.Availability.Dtos;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.Availability.Commands.SaveAvailabilityOverride;

public class SaveAvailabilityOverrideCommandHandler : IRequestHandler<SaveAvailabilityOverrideCommand, AvailabilityOverrideDto>
{
    private readonly IBookingTrackerDbContext _db;

    public SaveAvailabilityOverrideCommandHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<AvailabilityOverrideDto> Handle(SaveAvailabilityOverrideCommand request, CancellationToken cancellationToken)
    {
        // A fresh TimeRange per range, never a shared instance: these become an
        // owned collection, and EF Core identifies owned instances by reference.
        var ranges = request.Ranges.Select(r => TimeRange.Create(r.Start, r.End)).ToList();

        var existing = await _db.AvailabilityOverrides
            .FirstOrDefaultAsync(o => o.OrganizerId == request.OrganizerId && o.Date == request.Date, cancellationToken);

        if (existing is null)
        {
            existing = AvailabilityOverride.Create(request.OrganizerId, request.Date, ranges, request.Note);
            _db.AvailabilityOverrides.Add(existing);
        }
        else
        {
            // Scoped by OrganizerId in the lookup above, so this can only ever
            // find the caller's own row - there is no id from the client to
            // guess at, which is why this command needs no separate ownership
            // check the way the delete command does.
            existing.Update(ranges, request.Note);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return existing.ToDto();
    }
}
