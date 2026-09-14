using BookingTracker.Application.BookingSessions.Dtos;
using MediatR;

namespace BookingTracker.Application.BookingSessions.Commands.StartBookingSession;

public record StartBookingSessionCommand(string BookingPageSlug, string? ClientIp, string? UserAgent)
    : IRequest<BookingSessionDto>;
