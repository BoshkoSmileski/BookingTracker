using BookingTracker.Application.BookingSessions.Dtos;
using MediatR;

namespace BookingTracker.Application.Bookings.Queries.GetEmailHistory;

/// <summary>
/// Organizer-only. Reuses BookingSessionEventDto rather than a new shape -
/// email history is just a filtered view over the same event log everything
/// else in this app already reads from (EventType is EmailSent or ReminderSent).
/// </summary>
public record GetEmailHistoryQuery(Guid SessionId, Guid RequestingOrganizerId) : IRequest<IReadOnlyList<BookingSessionEventDto>>;
