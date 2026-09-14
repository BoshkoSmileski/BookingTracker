using BookingTracker.Application.Auth.Common;
using BookingTracker.Application.Auth.Dtos;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.Auth.Commands.RefreshAccessToken;

/// <summary>
/// Rotates refresh tokens on every use: the presented token is revoked and
/// linked to its replacement rather than reused, so a refresh token can only
/// ever be redeemed once - replay of a stolen-but-already-used token fails.
/// </summary>
public class RefreshAccessTokenCommandHandler : IRequestHandler<RefreshAccessTokenCommand, AuthResultDto>
{
    private readonly IBookingTrackerDbContext _db;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public RefreshAccessTokenCommandHandler(IBookingTrackerDbContext db, IJwtTokenGenerator jwtTokenGenerator)
    {
        _db = db;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<AuthResultDto> Handle(RefreshAccessTokenCommand request, CancellationToken cancellationToken)
    {
        var existingToken = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.Token == request.RefreshToken, cancellationToken)
            ?? throw new AuthenticationException("Invalid refresh token.");

        if (!existingToken.IsActive)
            throw new AuthenticationException("Refresh token is expired or has already been used.");

        var organizer = await _db.Organizers.FirstOrDefaultAsync(o => o.Id == existingToken.OrganizerId, cancellationToken)
            ?? throw new AuthenticationException("Organizer account no longer exists.");

        var (newRefreshTokenEntity, result) = AuthResultFactory.Build(_jwtTokenGenerator, organizer);
        existingToken.Revoke(newRefreshTokenEntity.Token);
        _db.RefreshTokens.Add(newRefreshTokenEntity);

        await _db.SaveChangesAsync(cancellationToken);
        return result;
    }
}
