using BookingTracker.Application.Auth.Dtos;
using MediatR;

namespace BookingTracker.Application.Auth.Commands.Login;

public record LoginCommand(string Email, string Password) : IRequest<AuthResultDto>;
