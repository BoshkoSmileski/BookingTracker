using BookingTracker.Application.Bookings.Dtos;
using MediatR;

namespace BookingTracker.Application.Bookings.Commands.RescheduleBooking;

/// <summary>Exactly one of SessionId/PublicToken should be set - see CancelBookingCommand for the pattern.</summary>
public record RescheduleBookingCommand(
    Guid? SessionId,
    string? PublicToken,
    Guid? RequestingOrganizerId,
    DateOnly NewDate,
    TimeOnly NewTime,
    string? ClientIp,
    string? UserAgent) : IRequest<BookingConfirmationDto>;
