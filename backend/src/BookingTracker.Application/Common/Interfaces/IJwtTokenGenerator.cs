using BookingTracker.Domain.Entities;

namespace BookingTracker.Application.Common.Interfaces;

public record AccessToken(string Value, DateTime ExpiresAt);

public interface IJwtTokenGenerator
{
    AccessToken GenerateAccessToken(Organizer organizer);

    /// <summary>Opaque, cryptographically random - carries no claims itself, just an identifier.</summary>
    string GenerateRefreshTokenValue();

    TimeSpan RefreshTokenLifetime { get; }
}
