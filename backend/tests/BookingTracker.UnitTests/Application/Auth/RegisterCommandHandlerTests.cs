using BookingTracker.Application.Auth.Commands.Register;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Domain.Entities;
using BookingTracker.UnitTests.TestSupport;

namespace BookingTracker.UnitTests.Application.Auth;

public class RegisterCommandHandlerTests
{
    private static (BookingTracker.Infrastructure.Persistence.BookingTrackerDbContext Db, RegisterCommandHandler Handler) CreateHandler()
    {
        var db = InMemoryDbContextFactory.Create();
        var handler = new RegisterCommandHandler(db, AuthTestSupport.CreatePasswordHasher(), AuthTestSupport.CreateJwtTokenGenerator());
        return (db, handler);
    }

    [Fact]
    public async Task NewEmail_CreatesOrganizer_AndIssuesTokens()
    {
        var (db, handler) = CreateHandler();

        var result = await handler.Handle(new RegisterCommand("Jane Doe", "jane@example.com", "Passw0rd!"), CancellationToken.None);

        Assert.Equal("Jane Doe", result.Name);
        Assert.Equal("jane@example.com", result.Email);
        Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(result.RefreshToken));
        Assert.Single(db.Organizers, o => o.Email == "jane@example.com");
        Assert.Single(db.RefreshTokens, t => t.OrganizerId == result.OrganizerId);
    }

    [Fact]
    public async Task PasswordIsHashed_NeverStoredAsPlaintext()
    {
        var (db, handler) = CreateHandler();

        await handler.Handle(new RegisterCommand("Jane Doe", "jane@example.com", "Passw0rd!"), CancellationToken.None);

        var stored = db.Organizers.Single(o => o.Email == "jane@example.com");
        Assert.NotEqual("Passw0rd!", stored.PasswordHash);
    }

    [Fact]
    public async Task DuplicateEmail_ThrowsValidationException()
    {
        var (_, handler) = CreateHandler();
        await handler.Handle(new RegisterCommand("Jane Doe", "jane@example.com", "Passw0rd!"), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new RegisterCommand("Someone Else", "jane@example.com", "Different1!"), CancellationToken.None));

        Assert.Contains("Email", ex.Errors.Keys);
    }
}
