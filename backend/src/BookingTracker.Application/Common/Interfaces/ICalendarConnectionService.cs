using BookingTracker.Application.Calendar.Dtos;
using BookingTracker.Domain.Enums;

namespace BookingTracker.Application.Common.Interfaces;

/// <summary>
/// Provider-agnostic orchestration of the connect/disconnect/token lifecycle.
/// Resolves the right ICalendarProvider internally (by CalendarConnection.Provider)
/// so callers - commands, ICalendarSyncService - never touch a provider directly.
/// </summary>
public interface ICalendarConnectionService
{
    Task<CalendarConnectionDto> ConnectAsync(
        Guid organizerId, CalendarProviderType provider, string authorizationCode, CancellationToken cancellationToken);

    Task DisconnectAsync(Guid organizerId, CancellationToken cancellationToken);

    Task<CalendarConnectionDto?> GetConnectionAsync(Guid organizerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExternalCalendarDto>> ListAvailableCalendarsAsync(Guid organizerId, CancellationToken cancellationToken);

    Task<CalendarConnectionDto> SelectCalendarAsync(
        Guid organizerId, string externalCalendarId, string externalCalendarName, CancellationToken cancellationToken);

    Task<CalendarConnectionDto> UpdateSyncSettingsAsync(
        Guid organizerId, bool importBusyEvents, bool exportBookings, CancellationToken cancellationToken);

    Task<CalendarConnectionDto> UpdateEventSettingsAsync(
        Guid organizerId, string eventTitleFormat, bool autoDeleteCancelledBookings, bool autoUpdateRescheduledBookings,
        int? defaultReminderMinutes, string eventVisibility, CancellationToken cancellationToken);

    /// <summary>Returns a decrypted, guaranteed-non-expired access token, refreshing first if needed. Null if the organizer has no connection, or it needs reauthorization.</summary>
    Task<string?> GetValidAccessTokenAsync(Guid organizerId, CancellationToken cancellationToken);

    /// <summary>
    /// Manual "Sync Now": refreshes the token if needed, verifies the selected calendar still
    /// exists, pulls a fresh batch of busy intervals to prove read access, and updates the
    /// connection's Last successful/failed sync timestamps accordingly. Never throws - failures
    /// are recorded on the returned DTO's Status/HealthStatus/LastSyncError instead.
    /// </summary>
    Task<CalendarConnectionDto> SyncNowAsync(Guid organizerId, CancellationToken cancellationToken);
}
