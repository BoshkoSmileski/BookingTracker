namespace BookingTracker.Application.Calendar.Dtos;

public record CalendarConnectionDto(
    Guid Id,
    string Provider,
    string ExternalAccountEmail,
    string ExternalCalendarId,
    string ExternalCalendarName,
    string Status,
    /// <summary>A friendlier, organizer-facing label for Status - e.g. "Needs Reauthentication" instead of "ReauthorizationRequired".</summary>
    string HealthStatus,
    string? LastSyncError,
    DateTime? LastSuccessfulSyncAtUtc,
    DateTime? LastFailedSyncAtUtc,
    bool ImportBusyEvents,
    bool ExportBookings,
    bool AutoDeleteCancelledBookings,
    bool AutoUpdateRescheduledBookings,
    int? DefaultReminderMinutes,
    string EventVisibility,
    string EventTitleFormat,
    int SyncedBookingCount);
