using BookingTracker.Application.Bookings.Dtos;
using MediatR;

namespace BookingTracker.Application.BookingSessions.Commands.SubmitBookingSession;

public record SubmitBookingSessionCommand(
    Guid SessionId,
    int ClientSequenceNumber,
    string? ClientIp,
    string? UserAgent) : IRequest<BookingConfirmationDto>;
