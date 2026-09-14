using BookingTracker.Infrastructure.Auth;
using Microsoft.Extensions.Options;

namespace BookingTracker.UnitTests.TestSupport;

/// <summary>
/// Auth command handler tests use the real JwtTokenGenerator/PasswordHasher
/// (not fakes/mocks) - both are pure, I/O-free algorithms, so using the real
/// implementations is more faithful than stubbing them out and costs nothing
/// in test speed or determinism.
/// </summary>
public static class AuthTestSupport
{
    public static JwtTokenGenerator CreateJwtTokenGenerator(int accessTokenMinutes = 15, int refreshTokenDays = 30) =>
        new(Options.Create(new JwtSettings
        {
            Secret = "unit-test-signing-secret-at-least-32-bytes-long!",
            Issuer = "BookingTracker.Tests",
            Audience = "BookingTracker.Tests.Client",
            AccessTokenMinutes = accessTokenMinutes,
            RefreshTokenDays = refreshTokenDays,
        }));

    public static PasswordHasher CreatePasswordHasher() => new();
}
