using BookingTracker.Domain.Common;

namespace BookingTracker.Domain.Entities;

public class Organizer : Entity<Guid>
{
    public string Name { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public DateTime CreatedAt { get; private set; }

    private Organizer() { }

    /// <summary>
    /// PasswordHash must already be computed by the caller (Application layer,
    /// via IPasswordHasher) - the Domain layer has no dependency on any hashing
    /// library and never handles a plaintext password.
    /// </summary>
    public static Organizer Register(string name, string email, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Password hash is required.", nameof(passwordHash));

        return new Organizer
        {
            Id = Guid.NewGuid(),
            Name = name,
            Email = email,
            PasswordHash = passwordHash,
            CreatedAt = DateTime.UtcNow
        };
    }
}
