namespace BookingTracker.Api.Common;

/// <summary>
/// Bound from the "RateLimiting" configuration section. Two independently
/// tunable buckets back the "AuthenticationPolicy" and "BookingPolicy" fixed
/// window limiters registered in Program.cs - defaults here only apply if the
/// section (or a given key) is missing from configuration entirely.
/// </summary>
public class RateLimitingSettings
{
    public const string SectionName = "RateLimiting";

    /// <summary>Login/register/refresh/logout - small burst allowance, brute-force resistant.</summary>
    public RateLimitPolicySettings Authentication { get; set; } = new() { PermitLimit = 5, WindowSeconds = 60 };

    /// <summary>Public booking wizard (start session/append events/submit) - sized for legitimate
    /// debounced typing/event batching while still bounding automated abuse.</summary>
    public RateLimitPolicySettings Booking { get; set; } = new() { PermitLimit = 60, WindowSeconds = 60 };
}

public class RateLimitPolicySettings
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
    public int QueueLimit { get; set; }
}
