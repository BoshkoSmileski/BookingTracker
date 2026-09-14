using BookingTracker.Application.BookingSessions.Dtos;
using BookingTracker.Domain.Enums;
using MediatR;

namespace BookingTracker.Application.BookingSessions.Queries.GetBookingSessions;

/// <summary>RequestingOrganizerId must own BookingPageId - enforced in the handler.</summary>
public record GetBookingSessionsQuery(Guid BookingPageId, Guid RequestingOrganizerId, BookingSessionStatus? Status)
    : IRequest<IReadOnlyList<BookingSessionDto>>;
