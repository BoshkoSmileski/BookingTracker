namespace BookingTracker.Domain.Enums;

public enum CalendarSyncStatus
{
    /// <summary>Tokens are valid and syncing normally.</summary>
    Connected = 0,

    /// <summary>The refresh token was rejected (e.g. the organizer revoked access at Google) - only a fresh Connect can fix this.</summary>
    ReauthorizationRequired = 1,

    /// <summary>A transient failure (network, rate limit, outage) - may resolve on the next sync attempt without organizer action.</summary>
    Error = 2,

    /// <summary>The selected calendar itself was deleted or is no longer accessible on the Google account - the organizer must pick a different calendar (not just retry or reconnect).</summary>
    CalendarNotFound = 3
}
