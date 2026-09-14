namespace BookingTracker.Domain.Enums;

/// <summary>
/// Which external calendar service a CalendarConnection talks to. Adding a
/// provider means adding a value here plus a new ICalendarProvider
/// implementation in Infrastructure - no Application-layer changes.
/// </summary>
public enum CalendarProviderType
{
    Google = 0
}
