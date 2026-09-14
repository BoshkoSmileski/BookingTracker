using BookingTracker.Application.Auth.Dtos;
using MediatR;

namespace BookingTracker.Application.Auth.Commands.Register;

public record RegisterCommand(string Name, string Email, string Password) : IRequest<AuthResultDto>;
