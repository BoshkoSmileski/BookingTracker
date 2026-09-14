using BookingTracker.Domain.Common;

namespace BookingTracker.Domain.ValueObjects;

/// <summary>
/// Request-origin metadata captured alongside session state and events.
/// An EF Core owned type - never has its own identity or table.
///
/// Unlike the booking form fields (name/email/phone/message), IP address and
/// User-Agent are never something a visitor fills in or can correct - they're
/// read straight off the HTTP request. Rejecting a booking outright because a
/// scripted client sent an oversized User-Agent header would be the wrong
/// trade-off, so this clamps to the column limits instead of throwing: it's
/// diagnostic metadata, not user input the visitor can be asked to fix.
/// </summary>
public sealed record ClientContext
{
    public string? IpAddress { get; }
    public string? UserAgent { get; }

    public static readonly ClientContext Unknown = new(null, null);

    public ClientContext(string? ipAddress, string? userAgent)
    {
        IpAddress = Truncate(ipAddress, BookingFieldLimits.ClientIpMaxLength);
        UserAgent = Truncate(userAgent, BookingFieldLimits.UserAgentMaxLength);
    }

    private static string? Truncate(string? value, int maxLength)
        => string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}
