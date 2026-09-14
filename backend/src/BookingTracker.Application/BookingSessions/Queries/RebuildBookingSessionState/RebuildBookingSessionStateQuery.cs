using BookingTracker.Application.BookingSessions.Dtos;
using MediatR;

namespace BookingTracker.Application.BookingSessions.Queries.RebuildBookingSessionState;

/// <summary>
/// Diagnostic/proof query: reconstructs a session's state using nothing but
/// its event log (BookingSession.Rebuild), independent of the BookingSessions
/// projection row. If this ever disagrees with GetBookingSessionQuery for the
/// same session, the projection has drifted from the log - a correctness bug.
/// </summary>
public record RebuildBookingSessionStateQuery(Guid SessionId) : IRequest<BookingSessionDto>;
