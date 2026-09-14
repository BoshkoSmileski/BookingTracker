using BookingTracker.Infrastructure.Auth;

namespace BookingTracker.UnitTests.Infrastructure.Auth;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void HashPassword_NeverReturnsThePlaintext()
    {
        var hash = _hasher.HashPassword("Passw0rd!");

        Assert.NotEqual("Passw0rd!", hash);
        Assert.NotEmpty(hash);
    }

    [Fact]
    public void HashPassword_SamePasswordTwice_ProducesDifferentHashes()
    {
        // PBKDF2 salts each hash independently - two hashes of the same password must never match byte-for-byte.
        var hash1 = _hasher.HashPassword("Passw0rd!");
        var hash2 = _hasher.HashPassword("Passw0rd!");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void VerifyPassword_CorrectPassword_ReturnsTrue()
    {
        var hash = _hasher.HashPassword("Passw0rd!");

        Assert.True(_hasher.VerifyPassword(hash, "Passw0rd!"));
    }

    [Fact]
    public void VerifyPassword_WrongPassword_ReturnsFalse()
    {
        var hash = _hasher.HashPassword("Passw0rd!");

        Assert.False(_hasher.VerifyPassword(hash, "SomethingElse!"));
    }

    [Fact]
    public void VerifyPassword_CaseSensitive()
    {
        var hash = _hasher.HashPassword("Passw0rd!");

        Assert.False(_hasher.VerifyPassword(hash, "passw0rd!"));
    }

    [Fact]
    public void VerifyPassword_EmptyOrWhitespaceCandidate_ReturnsFalse()
    {
        var hash = _hasher.HashPassword("Passw0rd!");

        Assert.False(_hasher.VerifyPassword(hash, ""));
    }
}
