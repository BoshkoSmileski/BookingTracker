namespace BookingTracker.Application.Common.Exceptions;

/// <summary>Invalid credentials or an invalid/expired/revoked token - maps to HTTP 401.</summary>
public class AuthenticationException : Exception
{
    public AuthenticationException(string message) : base(message) { }
}
