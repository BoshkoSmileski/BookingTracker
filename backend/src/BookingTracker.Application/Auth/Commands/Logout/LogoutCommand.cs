using MediatR;

namespace BookingTracker.Application.Auth.Commands.Logout;

public record LogoutCommand(string RefreshToken) : IRequest;
