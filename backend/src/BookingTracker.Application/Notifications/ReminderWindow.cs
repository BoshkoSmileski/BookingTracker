namespace BookingTracker.Application.Notifications;

/// <summary>
/// Turns a reminder lead time in minutes into the two strings the rest of the
/// system needs: a stable <c>Key</c> (recorded on the queued notification and
/// on the session's ReminderSent event, so it must never change for a given
/// lead time) and a human <c>Label</c> used in the email subject and in the
/// organizer's reminder list.
///
/// Lives in Application rather than in the sweeper because both the sweeper
/// (Infrastructure) and the reminder read model (Application) need identical
/// wording - a second copy would let the list say "1 hour" while the email
/// said something else.
/// </summary>
public static class ReminderWindow
{
    /// <summary>
    /// 60 and 1440 keep the "1h"/"24h" keys they had before reminder intervals
    /// became per-organizer, so ReminderSent events and queued notifications
    /// recorded by the older code still match and are never re-sent.
    /// </summary>
    public static (string Key, string Label) Format(int minutes) => minutes switch
    {
        60 => ("1h", "1 hour"),
        1440 => ("24h", "24 hours"),
        _ when minutes % 1440 == 0 => ($"{minutes}m", Pluralize(minutes / 1440, "day")),
        _ when minutes % 60 == 0 => ($"{minutes}m", Pluralize(minutes / 60, "hour")),
        _ => ($"{minutes}m", Pluralize(minutes, "minute")),
    };

    public static string Key(int minutes) => Format(minutes).Key;

    public static string Label(int minutes) => Format(minutes).Label;

    private static string Pluralize(int value, string unit) => $"{value} {unit}{(value == 1 ? "" : "s")}";
}
