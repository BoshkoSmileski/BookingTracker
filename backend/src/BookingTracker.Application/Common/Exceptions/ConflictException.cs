namespace BookingTracker.Application.Common.Exceptions;

/// <summary>The requested slot is no longer available - maps to HTTP 409.</summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}
