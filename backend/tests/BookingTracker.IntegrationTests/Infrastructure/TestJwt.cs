using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BookingTracker.Domain.Entities;
using Microsoft.IdentityModel.Tokens;

namespace BookingTracker.IntegrationTests.Infrastructure;

/// <summary>
/// Mints access tokens for authenticated test requests.
///
/// **There is deliberately no test authentication scheme.** The obvious move is
/// a stub AuthenticationHandler that injects claims, but that would replace the
/// exact code most worth covering at this layer: JwtBearer's validation, the
/// issuer/audience/lifetime/signature checks Program.cs configures, and
/// ClaimsPrincipalExtensions.GetOrganizerId - which is the single place that
/// knows how an organizer id is encoded in a claim. Signing a real token with
/// the test secret exercises all of it and needs no production change at all,
/// which makes it both the smaller and the stronger option.
///
/// The secret is test-only and lives only in this test host's in-memory
/// configuration; nothing here weakens production authorization.
/// </summary>
public static class TestJwt
{
    public const string Issuer = "BookingTracker.IntegrationTests";
    public const string Audience = "BookingTracker.IntegrationTests";

    /// <summary>Long enough for HMAC-SHA256, and obviously not a real secret.</summary>
    public const string Secret = "integration-tests-only-signing-key-do-not-use-anywhere-else-0123456789";

    public static string AccessTokenFor(Organizer organizer)
        => Build(organizer.Id.ToString(), organizer.Email, organizer.Name, DateTime.UtcNow.AddMinutes(15));

    /// <summary>An otherwise valid token for an organizer id that does not exist.</summary>
    public static string AccessTokenForUnknownOrganizer()
        => Build(Guid.NewGuid().ToString(), "ghost@example.com", "Ghost", DateTime.UtcNow.AddMinutes(15));

    /// <summary>Correctly signed, correctly shaped, and expired - so only the lifetime check can reject it.</summary>
    public static string ExpiredAccessTokenFor(Organizer organizer)
        => Build(organizer.Id.ToString(), organizer.Email, organizer.Name, DateTime.UtcNow.AddMinutes(-60));

    /// <summary>Right shape, wrong signing key - the signature check is the only thing that can catch it.</summary>
    public static string AccessTokenSignedWithTheWrongKey(Organizer organizer)
        => Build(organizer.Id.ToString(), organizer.Email, organizer.Name, DateTime.UtcNow.AddMinutes(15),
            secret: "a-completely-different-key-that-the-api-does-not-trust-0123456789");

    private static string Build(string subject, string email, string name, DateTime expiresUtc, string? secret = null)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret ?? Secret)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, subject),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim("name", name),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ],
            expires: expiresUtc,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
