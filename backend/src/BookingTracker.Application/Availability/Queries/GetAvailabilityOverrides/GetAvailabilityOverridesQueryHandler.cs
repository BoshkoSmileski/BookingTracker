using BookingTracker.Application.Availability.Dtos;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.Availability.Queries.GetAvailabilityOverrides;

public class GetAvailabilityOverridesQueryHandler
    : IRequestHandler<GetAvailabilityOverridesQuery, IReadOnlyList<AvailabilityOverrideDto>>
{
    private readonly IBookingTrackerDbContext _db;

    public GetAvailabilityOverridesQueryHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<IReadOnlyList<AvailabilityOverrideDto>> Handle(
        GetAvailabilityOverridesQuery request, CancellationToken cancellationToken)
    {
        // A plain date comparison, unlike the exceptions query's overlap test:
        // an override is a single day, so there is no range to reach into the
        // window from outside it.
        var query = _db.AvailabilityOverrides.AsNoTracking().Where(o => o.OrganizerId == request.OrganizerId);

        if (request.From is { } from) query = query.Where(o => o.Date >= from);
        if (request.To is { } to) query = query.Where(o => o.Date <= to);

        var overrides = await query.OrderBy(o => o.Date).ToListAsync(cancellationToken);
        return overrides.Select(o => o.ToDto()).ToList();
    }
}
