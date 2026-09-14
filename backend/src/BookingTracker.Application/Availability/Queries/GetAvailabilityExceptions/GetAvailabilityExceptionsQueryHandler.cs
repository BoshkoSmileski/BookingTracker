using BookingTracker.Application.Availability.Dtos;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.Availability.Queries.GetAvailabilityExceptions;

public class GetAvailabilityExceptionsQueryHandler
    : IRequestHandler<GetAvailabilityExceptionsQuery, IReadOnlyList<AvailabilityExceptionDto>>
{
    private readonly IBookingTrackerDbContext _db;

    public GetAvailabilityExceptionsQueryHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<IReadOnlyList<AvailabilityExceptionDto>> Handle(GetAvailabilityExceptionsQuery request, CancellationToken cancellationToken)
    {
        var query = _db.AvailabilityExceptions.AsNoTracking().Where(e => e.OrganizerId == request.OrganizerId);

        // Overlap rather than containment, matching GetAvailableSlotsQueryHandler:
        // a range that merely reaches into [from..to] is still in scope, or a
        // vacation would disappear from the list halfway through itself.
        if (request.From is { } from) query = query.Where(e => e.EndDate >= from);
        if (request.To is { } to) query = query.Where(e => e.Date <= to);

        var exceptions = await query.OrderBy(e => e.Date).ToListAsync(cancellationToken);
        return exceptions.Select(e => e.ToDto()).ToList();
    }
}
