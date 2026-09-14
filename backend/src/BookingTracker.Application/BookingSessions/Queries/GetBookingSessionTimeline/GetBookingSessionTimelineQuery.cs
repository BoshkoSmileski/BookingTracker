using BookingTracker.Application.BookingSessions.Dtos;
using MediatR;

namespace BookingTracker.Application.BookingSessions.Queries.GetBookingSessionTimeline;

/// <summary>
/// The full event log for one session, for the organizer who owns it.
///
/// RequestingOrganizerId is deliberately REQUIRED and non-nullable - it used to
/// be an optional <c>Guid?</c>, and the handler ran its ownership check only
/// when one was supplied. The anonymous route never supplied one, so the check
/// never ran, which made the credential leak reachable by anyone holding a session
/// id. That route is gone, and the parameter is now required so the unchecked
/// path cannot be reintroduced by simply omitting an argument.
/// </summary>
public record GetBookingSessionTimelineQuery(Guid SessionId, Guid RequestingOrganizerId)
    : IRequest<IReadOnlyList<BookingSessionEventDto>>;
