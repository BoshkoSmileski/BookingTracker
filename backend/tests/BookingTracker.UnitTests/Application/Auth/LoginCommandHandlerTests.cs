using BookingTracker.Application.Auth.Commands.Login;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Domain.Entities;
using BookingTracker.UnitTests.TestSupport;

namespace BookingTracker.UnitTests.Application.Auth;

public class LoginCommandHandlerTests
{
    private const string Password = "Passw0rd!";

    private static async Task<(BookingTracker.Infrastructure.Persistence.BookingTrackerDbContext Db, LoginCommandHandler Handler, Organizer Organizer)> Seed()
    {
        var db = InMemoryDbContextFactory.Create();
        var hasher = AuthTestSupport.CreatePasswordHasher();
        var jwt = AuthTestSupport.CreateJwtTokenGenerator();
        var organizer = Organizer.Register("Jane Doe", "jane@example.com", hasher.HashPassword(Password));
        db.Organizers.Add(organizer);
        await db.SaveChangesAsync();

        return (db, new LoginCommandHandler(db, hasher, jwt), organizer);
    }

    [Fact]
    public async Task ValidCredentials_ReturnsAuthResult_AndPersistsARefreshToken()
    {
        var (db, handler, organizer) = await Seed();

        var result = await handler.Handle(new LoginCommand(organizer.Email, Password), CancellationToken.None);

        Assert.Equal(organizer.Id, result.OrganizerId);
        Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(result.RefreshToken));
        Assert.Single(db.RefreshTokens, t => t.Token == result.RefreshToken && t.OrganizerId == organizer.Id);
    }

    [Fact]
    public async Task WrongPassword_ThrowsAuthenticationException()
    {
        var (_, handler, organizer) = await Seed();

        await Assert.ThrowsAsync<AuthenticationException>(() =>
            handler.Handle(new LoginCommand(organizer.Email, "WrongPassword!"), CancellationToken.None));
    }

    [Fact]
    public async Task UnknownEmail_ThrowsAuthenticationException_SameMessageAsWrongPassword()
    {
        // The message must not reveal whether the email exists - both failure modes throw the same generic error.
        var (_, handler, organizer) = await Seed();

        var unknownEmailEx = await Assert.ThrowsAsync<AuthenticationException>(() =>
            handler.Handle(new LoginCommand("nobody@example.com", Password), CancellationToken.None));
        var wrongPasswordEx = await Assert.ThrowsAsync<AuthenticationException>(() =>
            handler.Handle(new LoginCommand(organizer.Email, "WrongPassword!"), CancellationToken.None));

        Assert.Equal(wrongPasswordEx.Message, unknownEmailEx.Message);
    }
}
