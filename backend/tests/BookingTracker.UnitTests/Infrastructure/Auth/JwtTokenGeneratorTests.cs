using System.IdentityModel.Tokens.Jwt;
using BookingTracker.Domain.Entities;
using BookingTracker.Infrastructure.Auth;
using Microsoft.Extensions.Options;

namespace BookingTracker.UnitTests.Infrastructure.Auth;

public class JwtTokenGeneratorTests
{
    private static JwtTokenGenerator CreateGenerator(int accessTokenMinutes = 15, int refreshTokenDays = 30) =>
        new(Options.Create(new JwtSettings
        {
            Secret = "unit-test-signing-secret-at-least-32-bytes-long!",
            Issuer = "BookingTracker.Tests",
            Audience = "BookingTracker.Tests.Client",
            AccessTokenMinutes = accessTokenMinutes,
            RefreshTokenDays = refreshTokenDays,
        }));

    private static Organizer CreateOrganizer() => Organizer.Register("Jane Doe", "jane@example.com", "hash");

    [Fact]
    public void GenerateAccessToken_ProducesATokenSignedForTheCorrectOrganizer()
    {
        var generator = CreateGenerator();
        var organizer = CreateOrganizer();

        var token = generator.GenerateAccessToken(organizer);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.Value);
        Assert.Equal(organizer.Id.ToString(), jwt.Subject);
        Assert.Equal(organizer.Email, jwt.Claims.Single(c => c.Type == JwtRegisteredClaimNames.Email).Value);
        Assert.Equal(organizer.Name, jwt.Claims.Single(c => c.Type == "name").Value);
    }

    [Fact]
    public void GenerateAccessToken_ExpiresAccordingToConfiguredAccessTokenMinutes()
    {
        var generator = CreateGenerator(accessTokenMinutes: 15);
        var organizer = CreateOrganizer();
        var before = DateTime.UtcNow;

        var token = generator.GenerateAccessToken(organizer);

        var expectedExpiry = before.AddMinutes(15);
        Assert.True(Math.Abs((token.ExpiresAt - expectedExpiry).TotalSeconds) < 5, "Expiry should be ~15 minutes from now.");
    }

    [Fact]
    public void GenerateAccessToken_EachCallProducesAUniqueJti()
    {
        var generator = CreateGenerator();
        var organizer = CreateOrganizer();

        var token1 = generator.GenerateAccessToken(organizer);
        var token2 = generator.GenerateAccessToken(organizer);

        var jti1 = new JwtSecurityTokenHandler().ReadJwtToken(token1.Value).Claims.Single(c => c.Type == JwtRegisteredClaimNames.Jti).Value;
        var jti2 = new JwtSecurityTokenHandler().ReadJwtToken(token2.Value).Claims.Single(c => c.Type == JwtRegisteredClaimNames.Jti).Value;

        Assert.NotEqual(jti1, jti2);
    }

    [Fact]
    public void GenerateAccessToken_DifferentOrganizers_ProduceDifferentTokens()
    {
        var generator = CreateGenerator();

        var token1 = generator.GenerateAccessToken(CreateOrganizer());
        var token2 = generator.GenerateAccessToken(Organizer.Register("Other", "other@example.com", "hash"));

        Assert.NotEqual(token1.Value, token2.Value);
    }

    [Fact]
    public void GenerateRefreshTokenValue_ProducesALongRandomValue_UniquePerCall()
    {
        var generator = CreateGenerator();

        var token1 = generator.GenerateRefreshTokenValue();
        var token2 = generator.GenerateRefreshTokenValue();

        Assert.NotEqual(token1, token2);
        // 64 random bytes, base64-encoded, is comfortably longer than any guessable short value.
        Assert.True(token1.Length >= 64);
    }

    [Fact]
    public void RefreshTokenLifetime_MatchesConfiguredRefreshTokenDays()
    {
        var generator = CreateGenerator(refreshTokenDays: 30);

        Assert.Equal(TimeSpan.FromDays(30), generator.RefreshTokenLifetime);
    }
}
