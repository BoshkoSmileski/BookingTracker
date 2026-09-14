using BookingTracker.Application.BookingSessions.Dtos;
using MediatR;

namespace BookingTracker.Application.BookingSessions.Queries.GetBookingSession;

/// <summary>
/// RequestingOrganizerId is null for the public/visitor access path (unrestricted -
/// a visitor can always look up their own session). When supplied by an
/// organizer-only caller, the handler verifies that organizer owns the session's
/// booking page and throws ForbiddenException otherwise.
/// </summary>
public record GetBookingSessionQuery(Guid SessionId, Guid? RequestingOrganizerId = null) : IRequest<BookingSessionDto>;
