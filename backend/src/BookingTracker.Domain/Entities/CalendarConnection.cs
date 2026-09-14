using BookingTracker.Domain.Common;
using BookingTracker.Domain.Enums;

namespace BookingTracker.Domain.Entities;

/// <summary>
/// One per organizer (unique index on OrganizerId, same "one-per-organizer"
/// shape as WorkingSchedule), linking them to an external calendar account.
/// Access/refresh tokens are stored encrypted (Infrastructure's job, via Data
/// Protection) - this entity only ever holds the ciphertext, never a raw token.
/// </summary>
public class CalendarConnection : Entity<Guid>
{
    public const string DefaultEventTitleFormat = "{Service Name} with {Guest Name}";
    public const string DefaultEventVisibility = "default";

    public Guid OrganizerId { get; private set; }
    public CalendarProviderType Provider { get; private set; }
    public string ExternalAccountEmail { get; private set; } = default!;
    public string ExternalCalendarId { get; private set; } = default!;
    public string ExternalCalendarName { get; private set; } = default!;
    public string EncryptedAccessToken { get; private set; } = default!;
    public string EncryptedRefreshToken { get; private set; } = default!;
    public DateTime AccessTokenExpiresAtUtc { get; private set; }
    public CalendarSyncStatus Status { get; private set; }
    public string? LastSyncError { get; private set; }

    /// <summary>The most recent time a sync operation (Sync Now, a background check, or an actual booking sync) completed successfully.</summary>
    public DateTime? LastSuccessfulSyncAtUtc { get; private set; }

    /// <summary>The most recent time a sync operation failed - kept independently of LastSuccessfulSyncAtUtc so the organizer can see both, not just whichever happened last.</summary>
    public DateTime? LastFailedSyncAtUtc { get; private set; }

    public bool ImportBusyEvents { get; private set; }
    public bool ExportBookings { get; private set; }

    /// <summary>If false, cancelling a booking leaves its Google Calendar event in place instead of deleting it.</summary>
    public bool AutoDeleteCancelledBookings { get; private set; }

    /// <summary>If false, rescheduling a booking leaves its Google Calendar event at the old time instead of moving it.</summary>
    public bool AutoUpdateRescheduledBookings { get; private set; }

    /// <summary>Minutes before the event to show a popup reminder. Null = use the calendar's own default reminders.</summary>
    public int? DefaultReminderMinutes { get; private set; }

    /// <summary>Google Calendar event visibility: "default", "public", or "private" - stored pre-validated so the provider can pass it straight through.</summary>
    public string EventVisibility { get; private set; } = DefaultEventVisibility;

    /// <summary>Template for generated event titles. Supports {Service Name} and {Guest Name} placeholders.</summary>
    public string EventTitleFormat { get; private set; } = DefaultEventTitleFormat;

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private CalendarConnection() { }

    public static CalendarConnection Connect(
        Guid organizerId, CalendarProviderType provider, string externalAccountEmail,
        string externalCalendarId, string externalCalendarName,
        string encryptedAccessToken, string encryptedRefreshToken, DateTime accessTokenExpiresAtUtc)
    {
        return new CalendarConnection
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizerId,
            Provider = provider,
            ExternalAccountEmail = externalAccountEmail,
            ExternalCalendarId = externalCalendarId,
            ExternalCalendarName = externalCalendarName,
            EncryptedAccessToken = encryptedAccessToken,
            EncryptedRefreshToken = encryptedRefreshToken,
            AccessTokenExpiresAtUtc = accessTokenExpiresAtUtc,
            Status = CalendarSyncStatus.Connected,
            ImportBusyEvents = true,
            ExportBookings = true,
            AutoDeleteCancelledBookings = true,
            AutoUpdateRescheduledBookings = true,
            DefaultReminderMinutes = null,
            EventVisibility = DefaultEventVisibility,
            EventTitleFormat = DefaultEventTitleFormat,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void UpdateTokens(string encryptedAccessToken, string encryptedRefreshToken, DateTime accessTokenExpiresAtUtc)
    {
        EncryptedAccessToken = encryptedAccessToken;
        EncryptedRefreshToken = encryptedRefreshToken;
        AccessTokenExpiresAtUtc = accessTokenExpiresAtUtc;
        Status = CalendarSyncStatus.Connected;
        LastSyncError = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Used by a reconnect (re-authenticating an already-connected organizer,
    /// possibly with a different Google account) - a no-op if the account is
    /// unchanged, so a same-account reconnect (by far the common case: an
    /// expired/revoked token) doesn't touch this field or UpdatedAt at all.
    /// </summary>
    public void UpdateAccountEmail(string externalAccountEmail)
    {
        if (ExternalAccountEmail == externalAccountEmail) return;
        ExternalAccountEmail = externalAccountEmail;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SelectCalendar(string externalCalendarId, string externalCalendarName)
    {
        ExternalCalendarId = externalCalendarId;
        ExternalCalendarName = externalCalendarName;
        // A newly-picked calendar is, by definition, not missing - clear a stale
        // CalendarNotFound status so the health indicator reflects reality immediately
        // rather than waiting for the next sync.
        if (Status == CalendarSyncStatus.CalendarNotFound)
        {
            Status = CalendarSyncStatus.Connected;
            LastSyncError = null;
        }
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateSyncSettings(bool importBusyEvents, bool exportBookings)
    {
        ImportBusyEvents = importBusyEvents;
        ExportBookings = exportBookings;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateEventSettings(
        string eventTitleFormat, bool autoDeleteCancelledBookings, bool autoUpdateRescheduledBookings,
        int? defaultReminderMinutes, string eventVisibility)
    {
        EventTitleFormat = eventTitleFormat;
        AutoDeleteCancelledBookings = autoDeleteCancelledBookings;
        AutoUpdateRescheduledBookings = autoUpdateRescheduledBookings;
        DefaultReminderMinutes = defaultReminderMinutes;
        EventVisibility = eventVisibility;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkSyncSucceeded(DateTime atUtc)
    {
        Status = CalendarSyncStatus.Connected;
        LastSyncError = null;
        LastSuccessfulSyncAtUtc = atUtc;
    }

    public void MarkSyncFailed(string error, bool requiresReauthorization) => MarkSyncFailed(error, requiresReauthorization, calendarNotFound: false);

    public void MarkSyncFailed(string error, bool requiresReauthorization, bool calendarNotFound)
    {
        Status = calendarNotFound ? CalendarSyncStatus.CalendarNotFound
            : requiresReauthorization ? CalendarSyncStatus.ReauthorizationRequired
            : CalendarSyncStatus.Error;
        LastSyncError = error;
        LastFailedSyncAtUtc = DateTime.UtcNow;
    }
}
