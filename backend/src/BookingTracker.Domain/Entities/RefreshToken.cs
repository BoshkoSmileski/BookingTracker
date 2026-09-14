using BookingTracker.Domain.Common;

namespace BookingTracker.Domain.Entities;

/// <summary>
/// An issued JWT refresh token. Rotated (not reused) on every refresh: using
/// one marks it Revoked and links to its replacement, so a stolen, already-used
/// token is detectable and permanently dead rather than silently accepted.
/// </summary>
public class RefreshToken : Entity<Guid>
{
    public Guid OrganizerId { get; private set; }
    public string Token { get; private set; } = default!;
    public DateTime ExpiresAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public string? ReplacedByToken { get; private set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsActive => RevokedAt is null && !IsExpired;

    private RefreshToken() { }

    public static RefreshToken Issue(Guid organizerId, string token, DateTime expiresAt)
    {
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizerId,
            Token = token,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Revoke(string? replacedByToken = null)
    {
        RevokedAt = DateTime.UtcNow;
        ReplacedByToken = replacedByToken;
    }
}
