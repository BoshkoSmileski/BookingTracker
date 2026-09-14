namespace BookingTracker.Application.Common.Interfaces;

/// <summary>
/// Abstracts password hashing away from the concrete algorithm. Implemented in
/// Infrastructure by wrapping Microsoft.AspNetCore.Identity's PasswordHasher,
/// so Organizer credentials use the same PBKDF2 implementation ASP.NET Identity
/// ships with, without pulling in the full IdentityDbContext/UserManager stack.
/// </summary>
public interface IPasswordHasher
{
    string HashPassword(string password);

    bool VerifyPassword(string hashedPassword, string providedPassword);
}
