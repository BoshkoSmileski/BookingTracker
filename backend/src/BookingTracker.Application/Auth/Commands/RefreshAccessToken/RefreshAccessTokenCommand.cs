using BookingTracker.Application.Auth.Dtos;
using MediatR;

namespace BookingTracker.Application.Auth.Commands.RefreshAccessToken;

public record RefreshAccessTokenCommand(string RefreshToken) : IRequest<AuthResultDto>;
