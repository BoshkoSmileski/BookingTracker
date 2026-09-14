namespace BookingTracker.Application.Common.Exceptions;

/// <summary>Authenticated, but not the owner of the resource being accessed - maps to HTTP 403.</summary>
public class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message) { }
}
