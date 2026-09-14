using BookingTracker.Application.Auth.Dtos;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Domain.Entities;

namespace BookingTracker.Application.Auth.Common;

/// <summary>
/// Shared by Register/Login/RefreshAccessToken so token issuance logic exists
/// in exactly one place. Callers are responsible for adding the returned
/// RefreshToken to the DbContext and calling SaveChangesAsync - this factory
/// does no I/O itself.
/// </summary>
public static class AuthResultFactory
{
    public static (RefreshToken RefreshTokenEntity, AuthResultDto Result) Build(IJwtTokenGenerator jwtTokenGenerator, Organizer organizer)
    {
        var accessToken = jwtTokenGenerator.GenerateAccessToken(organizer);
        var refreshTokenValue = jwtTokenGenerator.GenerateRefreshTokenValue();
        var refreshTokenExpiresAt = DateTime.UtcNow.Add(jwtTokenGenerator.RefreshTokenLifetime);

        var refreshTokenEntity = RefreshToken.Issue(organizer.Id, refreshTokenValue, refreshTokenExpiresAt);

        var result = new AuthResultDto(
            organizer.Id,
            organizer.Name,
            organizer.Email,
            accessToken.Value,
            accessToken.ExpiresAt,
            refreshTokenValue,
            refreshTokenExpiresAt);

        return (refreshTokenEntity, result);
    }
}
