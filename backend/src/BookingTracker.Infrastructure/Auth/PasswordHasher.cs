using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace BookingTracker.Infrastructure.Auth;

/// <summary>
/// Wraps ASP.NET Core Identity's own PasswordHasher (PBKDF2, timing-safe
/// verification) instead of the full Identity Store/UserManager stack - the
/// Organizer entity already models what the store would, so there's no need
/// for a second, competing user table.
/// </summary>
public class PasswordHasher : IPasswordHasher
{
    private readonly Microsoft.AspNetCore.Identity.PasswordHasher<Organizer> _inner = new();

    public string HashPassword(string password) => _inner.HashPassword(null!, password);

    public bool VerifyPassword(string hashedPassword, string providedPassword)
    {
        var result = _inner.VerifyHashedPassword(null!, hashedPassword, providedPassword);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
