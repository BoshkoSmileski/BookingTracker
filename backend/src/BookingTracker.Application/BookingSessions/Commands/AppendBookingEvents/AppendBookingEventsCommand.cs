using BookingTracker.Application.BookingSessions.Dtos;
using MediatR;

namespace BookingTracker.Application.BookingSessions.Commands.AppendBookingEvents;

public record AppendBookingEventsCommand(
    Guid SessionId,
    IReadOnlyList<ClientBookingEventDto> Events,
    string? ClientIp,
    string? UserAgent) : IRequest<BookingSessionDto>;
