using BookingTracker.Application.Bookings.Dtos;
using BookingTracker.Domain.Enums;
using MediatR;

namespace BookingTracker.Application.Bookings.Commands.CancelBooking;

/// <summary>
/// Exactly one of SessionId/PublicToken should be set - the public controller
/// resolves by token (no auth), the organizer controller by id (after an
/// ownership check via RequestingOrganizerId), same pattern as
/// GetBookingSessionQuery's dual public/organizer access.
/// </summary>
public record CancelBookingCommand(
    Guid? SessionId,
    string? PublicToken,
    Guid? RequestingOrganizerId,
    CancelledByType CancelledBy,
    string? Reason,
    string? ClientIp,
    string? UserAgent) : IRequest<BookingConfirmationDto>;
