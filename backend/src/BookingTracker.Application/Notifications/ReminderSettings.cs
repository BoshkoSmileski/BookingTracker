namespace BookingTracker.Application.Notifications;

/// <summary>
/// Tuning knobs for reminder scheduling and the sweep that fires them, bound
/// from the "Reminders" configuration section. Application-owned (like
/// EmailNotificationSettings) so nothing here forces Application to reference
/// Infrastructure just to read a number.
/// </summary>
public class ReminderSettings
{
    public const string SectionName = "Reminders";

    /// <summary>How often the sweeper looks for due reminders. Also the worst-case lateness of an on-time reminder, so it is deliberately much tighter than the old 5-minute reminder sweep: a 15-minute reminder fired up to 5 minutes late is visibly wrong.</summary>
    public int SweepIntervalSeconds { get; set; } = 60;

    /// <summary>Maximum reminders converted into notifications per sweep. Bounds both the query and the work done inside one transaction; leftovers are simply picked up by the next sweep.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// How late a reminder may still be delivered. Covers the "app was down at
    /// 09:00, started at 09:20" case: within this window the reminder is still
    /// useful and is sent; past it, it is marked Skipped rather than mailing
    /// someone about a meeting that is nearly upon them.
    /// </summary>
    public int GracePeriodMinutes { get; set; } = 60;

    /// <summary>
    /// Hard floor on how far back the sweeper will even look for unprocessed
    /// reminders. Distinct from GracePeriodMinutes, which decides send-vs-skip
    /// for reminders it found: this bounds the scan itself so the query cost
    /// stays flat as reminder history accumulates, and acts as a backstop if
    /// the grace period is ever misconfigured to something enormous. Anything
    /// older is left untouched (already terminal in practice) rather than
    /// rewritten on every sweep.
    /// </summary>
    public int MaxReminderAgeHours { get; set; } = 24;
}
