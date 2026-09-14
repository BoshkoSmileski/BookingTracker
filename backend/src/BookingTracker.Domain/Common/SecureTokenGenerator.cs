using System.Security.Cryptography;
using System.Text;

namespace BookingTracker.Domain.Common;

/// <summary>
/// Generates the two identifiers a confirmed booking gets. Both are plain BCL
/// cryptography (System.Security.Cryptography), so this stays in Domain
/// without pulling in any external dependency.
/// </summary>
public static class SecureTokenGenerator
{
    // Crockford-style alphabet with ambiguous characters (0/O, 1/I/L) removed,
    // so a human reading a reference number aloud can't misread it.
    private const string ReferenceAlphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    /// <summary>Short, human-friendly support reference, e.g. "X8QW-3PNK-72AL". Not a secret.</summary>
    public static string GenerateBookingReference()
    {
        var groups = new string[3];
        for (var g = 0; g < 3; g++)
        {
            var chars = new char[4];
            var randomBytes = RandomNumberGenerator.GetBytes(4);
            for (var i = 0; i < 4; i++)
            {
                chars[i] = ReferenceAlphabet[randomBytes[i] % ReferenceAlphabet.Length];
            }
            groups[g] = new string(chars);
        }
        return string.Join('-', groups);
    }

    /// <summary>
    /// Long, cryptographically random secret used as the sole credential for
    /// public self-service booking management - deliberately not derived from
    /// or equal to the database id, so knowing a session's internal Guid never
    /// grants access to cancel/reschedule it.
    /// </summary>
    public static string GeneratePublicToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var builder = new StringBuilder(64);
        foreach (var b in bytes) builder.Append(b.ToString("X2"));
        return builder.ToString();
    }
}
