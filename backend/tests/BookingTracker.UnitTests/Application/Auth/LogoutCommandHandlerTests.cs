using BookingTracker.Application.Auth.Commands.Logout;
using BookingTracker.Domain.Entities;
using BookingTracker.UnitTests.TestSupport;

namespace BookingTracker.UnitTests.Application.Auth;

public class LogoutCommandHandlerTests
{
    [Fact]
    public async Task ActiveToken_IsRevoked()
    {
        var db = InMemoryDbContextFactory.Create();
        var organizer = Organizer.Register("Jane Doe", "jane@example.com", "hash");
        var token = RefreshToken.Issue(organizer.Id, "refresh-token-value", DateTime.UtcNow.AddDays(30));
        db.Organizers.Add(organizer);
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();
        var handler = new LogoutCommandHandler(db);

        await handler.Handle(new LogoutCommand(token.Token), CancellationToken.None);

        var stored = db.RefreshTokens.Single(t => t.Token == token.Token);
        Assert.NotNull(stored.RevokedAt);
        Assert.False(stored.IsActive);
    }

    [Fact]
    public async Task UnknownToken_IsIdempotent_DoesNotThrow()
    {
        var db = InMemoryDbContextFactory.Create();
        var handler = new LogoutCommandHandler(db);

        // Per LogoutCommandHandler's own contract: revoking an unknown/already-revoked token is not an error.
        await handler.Handle(new LogoutCommand("never-issued"), CancellationToken.None);
    }

    [Fact]
    public async Task AlreadyRevokedToken_LogoutAgain_IsIdempotent_DoesNotThrowOrChangeRevokedAt()
    {
        var db = InMemoryDbContextFactory.Create();
        var organizer = Organizer.Register("Jane Doe", "jane@example.com", "hash");
        var token = RefreshToken.Issue(organizer.Id, "refresh-token-value", DateTime.UtcNow.AddDays(30));
        token.Revoke();
        var firstRevokedAt = token.RevokedAt;
        db.Organizers.Add(organizer);
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();
        var handler = new LogoutCommandHandler(db);

        await handler.Handle(new LogoutCommand(token.Token), CancellationToken.None);

        var stored = db.RefreshTokens.Single(t => t.Token == token.Token);
        Assert.Equal(firstRevokedAt, stored.RevokedAt);
    }
}
