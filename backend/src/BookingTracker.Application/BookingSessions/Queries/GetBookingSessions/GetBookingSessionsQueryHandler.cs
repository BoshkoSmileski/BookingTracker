using BookingTracker.Application.BookingSessions.Dtos;
using BookingTracker.Application.Common.Authorization;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.BookingSessions.Queries.GetBookingSessions;

/// <summary>
/// Backs the organizer dashboard's session list (Active / Submitted / Abandoned
/// tabs). Reads exclusively from the BookingSessions projection table - no event
/// aggregation needed, which is the whole point of keeping a materialized read model.
/// </summary>
public class GetBookingSessionsQueryHandler : IRequestHandler<GetBookingSessionsQuery, IReadOnlyList<BookingSessionDto>>
{
    private readonly IBookingTrackerDbContext _db;

    public GetBookingSessionsQueryHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<IReadOnlyList<BookingSessionDto>> Handle(GetBookingSessionsQuery request, CancellationToken cancellationToken)
    {
        await OwnershipGuard.EnsureOrganizerOwnsBookingPageAsync(_db, request.BookingPageId, request.RequestingOrganizerId, cancellationToken);

        var query = _db.BookingSessions
            .AsNoTracking()
            .Where(s => s.BookingPageId == request.BookingPageId);

        if (request.Status is not null)
        {
            query = query.Where(s => s.Status == request.Status);
        }

        var sessions = await query
            .OrderByDescending(s => s.LastActivityAt)
            .ToListAsync(cancellationToken);

        return sessions.Select(s => s.ToDto()).ToList();
    }
}
