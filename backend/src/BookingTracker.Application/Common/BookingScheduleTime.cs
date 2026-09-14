namespace BookingTracker.Application.Common;

/// <summary>
/// Converts a booking's organizer-local wall-clock date/time into the UTC
/// instant it actually happens at. Extracted because this exact pair of steps
/// (resolve the organizer's IANA/Windows zone id, falling back to UTC if the
/// host doesn't know it; convert an Unspecified-kind DateTime through it) was
/// already written out twice - in RescheduleBookingCommandHandler's
/// past-check and BookingReminderSweeper - and reminder scheduling would have
/// made it three copies of a calculation that must agree everywhere or
/// reminders fire at the wrong time. Static helper in Application/Common,
/// same shape as BookingConflictChecker.
/// </summary>
public static class BookingScheduleTime
{
    /// <summary>Falls back to UTC for an unknown/invalid zone id rather than throwing - a bad stored timezone must not take down a sweep or block a reschedule.</summary>
    public static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }

    public static DateTime ToUtc(DateOnly localDate, TimeOnly localTime, TimeZoneInfo timeZone)
    {
        var local = DateTime.SpecifyKind(localDate.ToDateTime(localTime), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
    }

    public static DateTime ToUtc(DateOnly localDate, TimeOnly localTime, string? timeZoneId) =>
        ToUtc(localDate, localTime, ResolveTimeZone(timeZoneId));
}
