using BookingTracker.Application.Auth.Commands.RefreshAccessToken;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Domain.Entities;
using BookingTracker.UnitTests.TestSupport;

namespace BookingTracker.UnitTests.Application.Auth;

/// <summary>Covers refresh token rotation and reuse rejection - the core defense against a stolen refresh token being replayed.</summary>
public class RefreshAccessTokenCommandHandlerTests
{
    private static async Task<(BookingTracker.Infrastructure.Persistence.BookingTrackerDbContext Db, RefreshAccessTokenCommandHandler Handler, Organizer Organizer, RefreshToken Token)>
        Seed(DateTime? expiresAt = null)
    {
        var db = InMemoryDbContextFactory.Create();
        var jwt = AuthTestSupport.CreateJwtTokenGenerator();
        var organizer = Organizer.Register("Jane Doe", "jane@example.com", "hash");
        var token = RefreshToken.Issue(organizer.Id, "original-refresh-token-value", expiresAt ?? DateTime.UtcNow.AddDays(30));
        db.Organizers.Add(organizer);
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();

        return (db, new RefreshAccessTokenCommandHandler(db, jwt), organizer, token);
    }

    [Fact]
    public async Task ValidActiveToken_IssuesANewAccessAndRefreshTokenPair()
    {
        var (_, handler, organizer, token) = await Seed();

        var result = await handler.Handle(new RefreshAccessTokenCommand(token.Token), CancellationToken.None);

        Assert.Equal(organizer.Id, result.OrganizerId);
        Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
        Assert.NotEqual(token.Token, result.RefreshToken);
    }

    [Fact]
    public async Task ValidActiveToken_RevokesTheOldTokenAndLinksItToTheReplacement_Rotation()
    {
        var (db, handler, _, token) = await Seed();

        var result = await handler.Handle(new RefreshAccessTokenCommand(token.Token), CancellationToken.None);

        var oldTokenAfterRefresh = db.RefreshTokens.Single(t => t.Token == token.Token);
        Assert.NotNull(oldTokenAfterRefresh.RevokedAt);
        Assert.Equal(result.RefreshToken, oldTokenAfterRefresh.ReplacedByToken);
        Assert.False(oldTokenAfterRefresh.IsActive);

        var newToken = db.RefreshTokens.Single(t => t.Token == result.RefreshToken);
        Assert.True(newToken.IsActive);
    }

    [Fact]
    public async Task AlreadyUsedToken_ReplayIsRejected_ReuseRejection()
    {
        var (_, handler, _, token) = await Seed();

        // First redemption succeeds and rotates the token...
        await handler.Handle(new RefreshAccessTokenCommand(token.Token), CancellationToken.None);

        // ...so replaying the SAME (now-revoked) token again must fail, exactly as it would
        // if a stolen, already-used token were replayed by an attacker.
        await Assert.ThrowsAsync<AuthenticationException>(() =>
            handler.Handle(new RefreshAccessTokenCommand(token.Token), CancellationToken.None));
    }

    [Fact]
    public async Task ExpiredToken_IsRejected()
    {
        var (_, handler, _, token) = await Seed(expiresAt: DateTime.UtcNow.AddDays(-1));

        await Assert.ThrowsAsync<AuthenticationException>(() =>
            handler.Handle(new RefreshAccessTokenCommand(token.Token), CancellationToken.None));
    }

    [Fact]
    public async Task UnknownTokenValue_IsRejected()
    {
        var (_, handler, _, _) = await Seed();

        await Assert.ThrowsAsync<AuthenticationException>(() =>
            handler.Handle(new RefreshAccessTokenCommand("this-token-was-never-issued"), CancellationToken.None));
    }
}
