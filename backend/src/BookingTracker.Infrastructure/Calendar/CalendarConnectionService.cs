using System.Diagnostics;
using BookingTracker.Application.Calendar.Dtos;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using Google.Apis.Auth.OAuth2.Responses;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookingTracker.Infrastructure.Calendar;

public class CalendarConnectionService : ICalendarConnectionService
{
    /// <summary>Refresh proactively this far ahead of real expiry, so a request never races an about-to-expire token.</summary>
    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromMinutes(2);

    private readonly IBookingTrackerDbContext _db;
    private readonly IEnumerable<ICalendarProvider> _providers;
    private readonly IDataProtector _protector;
    private readonly ILogger<CalendarConnectionService> _logger;

    public CalendarConnectionService(
        IBookingTrackerDbContext db,
        IEnumerable<ICalendarProvider> providers,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<CalendarConnectionService> logger)
    {
        _db = db;
        _providers = providers;
        _protector = dataProtectionProvider.CreateProtector("BookingTracker.CalendarTokens");
        _logger = logger;
    }

    public async Task<CalendarConnectionDto> ConnectAsync(
        Guid organizerId, CalendarProviderType providerType, string authorizationCode, CancellationToken cancellationToken)
    {
        var provider = ResolveProvider(providerType);
        // Throws (and leaves any existing connection completely untouched, since nothing
        // below has loaded or mutated it yet) if the exchange fails for any reason,
        // including Google not returning a refresh token.
        var oauthResult = await provider.ExchangeAuthorizationCodeAsync(authorizationCode, cancellationToken);

        var existing = await _db.CalendarConnections.FirstOrDefaultAsync(c => c.OrganizerId == organizerId, cancellationToken);
        if (existing is not null)
        {
            // Reconnect: update the existing row in place rather than delete-and-recreate.
            // CalendarSyncedEvents carries a foreign key to CalendarConnection.Id with
            // ON DELETE CASCADE (CalendarSyncedEventConfiguration) specifically so an
            // orphaned connection can never leave dangling event mappings behind - but that
            // same cascade meant the old delete-then-insert here destroyed every synced
            // booking's mapping to its Google event on every reconnect, silently turning
            // the next reschedule into a duplicate-event bug and the next cancellation into
            // an orphaned-event bug. Keeping
            // the same Id fixes both, and also means synchronization settings, event
            // settings, and the previously selected calendar survive untouched - reconnect's
            // job is only to refresh credentials, not to redo configuration the organizer
            // already made (that's SelectCalendarAsync/UpdateSyncSettingsAsync/
            // UpdateEventSettingsAsync's job). If the organizer authenticated a different
            // Google account, ExternalAccountEmail is updated to match (a no-op otherwise);
            // ExternalCalendarId is deliberately left as-is even then; if it doesn't resolve
            // under the new account, the existing CalendarNotFound detection (SyncNowAsync /
            // the background sweeper) already surfaces that on the next sync check, the same
            // way it would for any other calendar that became unavailable.
            existing.UpdateAccountEmail(oauthResult.AccountEmail);
            existing.UpdateTokens(
                _protector.Protect(oauthResult.AccessToken), _protector.Protect(oauthResult.RefreshToken), oauthResult.ExpiresAtUtc);

            await _db.SaveChangesAsync(cancellationToken);
            return await ToDtoAsync(existing, cancellationToken);
        }

        // First-time connect: default to the account's primary calendar - the organizer
        // can pick a different one later (the calendar picker).
        string calendarId = "primary";
        string calendarName = "Primary Calendar";
        try
        {
            var calendars = await provider.ListCalendarsAsync(oauthResult.AccessToken, cancellationToken);
            var primary = calendars.FirstOrDefault(c => c.IsPrimary) ?? calendars.FirstOrDefault();
            if (primary is not null)
            {
                calendarId = primary.Id;
                calendarName = primary.Name;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not list calendars right after connecting for organizer {OrganizerId}; defaulting to the primary calendar.", organizerId);
        }

        var connection = CalendarConnection.Connect(
            organizerId, providerType, oauthResult.AccountEmail, calendarId, calendarName,
            _protector.Protect(oauthResult.AccessToken), _protector.Protect(oauthResult.RefreshToken), oauthResult.ExpiresAtUtc);

        _db.CalendarConnections.Add(connection);
        await _db.SaveChangesAsync(cancellationToken);

        return await ToDtoAsync(connection, cancellationToken);
    }

    public async Task DisconnectAsync(Guid organizerId, CancellationToken cancellationToken)
    {
        var connection = await _db.CalendarConnections.FirstOrDefaultAsync(c => c.OrganizerId == organizerId, cancellationToken);
        if (connection is null) return;

        _db.CalendarConnections.Remove(connection);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CalendarConnectionDto?> GetConnectionAsync(Guid organizerId, CancellationToken cancellationToken)
    {
        var connection = await _db.CalendarConnections.AsNoTracking().FirstOrDefaultAsync(c => c.OrganizerId == organizerId, cancellationToken);
        return connection is null ? null : await ToDtoAsync(connection, cancellationToken);
    }

    public async Task<IReadOnlyList<ExternalCalendarDto>> ListAvailableCalendarsAsync(Guid organizerId, CancellationToken cancellationToken)
    {
        var connection = await _db.CalendarConnections.AsNoTracking().FirstOrDefaultAsync(c => c.OrganizerId == organizerId, cancellationToken)
            ?? throw new NotFoundException(nameof(CalendarConnection), organizerId);

        var accessToken = await GetValidAccessTokenAsync(organizerId, cancellationToken)
            ?? throw new ConflictException("This calendar connection needs to be reconnected before its calendars can be listed.");

        var provider = ResolveProvider(connection.Provider);
        return await provider.ListCalendarsAsync(accessToken, cancellationToken);
    }

    public async Task<CalendarConnectionDto> SelectCalendarAsync(
        Guid organizerId, string externalCalendarId, string externalCalendarName, CancellationToken cancellationToken)
    {
        var connection = await _db.CalendarConnections.FirstOrDefaultAsync(c => c.OrganizerId == organizerId, cancellationToken)
            ?? throw new NotFoundException(nameof(CalendarConnection), organizerId);

        connection.SelectCalendar(externalCalendarId, externalCalendarName);
        await _db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(connection, cancellationToken);
    }

    public async Task<CalendarConnectionDto> UpdateSyncSettingsAsync(
        Guid organizerId, bool importBusyEvents, bool exportBookings, CancellationToken cancellationToken)
    {
        var connection = await _db.CalendarConnections.FirstOrDefaultAsync(c => c.OrganizerId == organizerId, cancellationToken)
            ?? throw new NotFoundException(nameof(CalendarConnection), organizerId);

        connection.UpdateSyncSettings(importBusyEvents, exportBookings);
        await _db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(connection, cancellationToken);
    }

    public async Task<CalendarConnectionDto> UpdateEventSettingsAsync(
        Guid organizerId, string eventTitleFormat, bool autoDeleteCancelledBookings, bool autoUpdateRescheduledBookings,
        int? defaultReminderMinutes, string eventVisibility, CancellationToken cancellationToken)
    {
        var connection = await _db.CalendarConnections.FirstOrDefaultAsync(c => c.OrganizerId == organizerId, cancellationToken)
            ?? throw new NotFoundException(nameof(CalendarConnection), organizerId);

        connection.UpdateEventSettings(eventTitleFormat, autoDeleteCancelledBookings, autoUpdateRescheduledBookings, defaultReminderMinutes, eventVisibility);
        await _db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(connection, cancellationToken);
    }

    public async Task<CalendarConnectionDto> SyncNowAsync(Guid organizerId, CancellationToken cancellationToken)
    {
        var connection = await _db.CalendarConnections.FirstOrDefaultAsync(c => c.OrganizerId == organizerId, cancellationToken)
            ?? throw new NotFoundException(nameof(CalendarConnection), organizerId);

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Sync Now started for organizer {OrganizerId}.", organizerId);

        var accessToken = await GetValidAccessTokenAsync(organizerId, cancellationToken);
        if (accessToken is null)
        {
            // GetValidAccessTokenAsync already classified and recorded the failure (reauth
            // required, or a transient refresh error) - nothing more to do here except report it.
            stopwatch.Stop();
            _logger.LogWarning("Sync Now finished for organizer {OrganizerId} in {ElapsedMs}ms: could not obtain a valid access token (status={Status}).",
                organizerId, stopwatch.ElapsedMilliseconds, connection.Status);
            return await ToDtoAsync(connection, cancellationToken);
        }

        try
        {
            var provider = ResolveProvider(connection.Provider);

            // "Verify calendar access" - list the account's calendars and confirm the one
            // we're supposed to sync against is still among them (it may have been deleted,
            // or access to it specifically revoked, independent of the OAuth grant itself).
            var calendars = await provider.ListCalendarsAsync(accessToken, cancellationToken);
            if (!calendars.Any(c => c.Id == connection.ExternalCalendarId))
            {
                connection.MarkSyncFailed(
                    $"The calendar \"{connection.ExternalCalendarName}\" no longer exists on this Google account. Choose a different calendar.",
                    requiresReauthorization: false, calendarNotFound: true);
                await _db.SaveChangesAsync(cancellationToken);
                stopwatch.Stop();
                _logger.LogWarning("Sync Now finished for organizer {OrganizerId} in {ElapsedMs}ms: calendar {CalendarId} not found.",
                    organizerId, stopwatch.ElapsedMilliseconds, connection.ExternalCalendarId);
                return await ToDtoAsync(connection, cancellationToken);
            }

            // "Pull the latest busy events" - a real read against the selected calendar,
            // proving end-to-end access beyond just "the token still refreshes."
            await provider.GetBusyIntervalsAsync(
                accessToken, connection.ExternalCalendarId, DateTime.UtcNow, DateTime.UtcNow.AddDays(30), cancellationToken);

            connection.MarkSyncSucceeded(DateTime.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
            stopwatch.Stop();
            _logger.LogInformation("Sync Now finished for organizer {OrganizerId} in {ElapsedMs}ms: OK.", organizerId, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            RecordFailure(connection, ex);
            await _db.SaveChangesAsync(cancellationToken);
            stopwatch.Stop();
            _logger.LogError(ex, "Sync Now finished for organizer {OrganizerId} in {ElapsedMs}ms: FAILED.", organizerId, stopwatch.ElapsedMilliseconds);
        }

        return await ToDtoAsync(connection, cancellationToken);
    }

    public async Task<string?> GetValidAccessTokenAsync(Guid organizerId, CancellationToken cancellationToken)
    {
        var connection = await _db.CalendarConnections.FirstOrDefaultAsync(c => c.OrganizerId == organizerId, cancellationToken);
        if (connection is null || connection.Status == CalendarSyncStatus.ReauthorizationRequired) return null;

        if (connection.AccessTokenExpiresAtUtc > DateTime.UtcNow.Add(RefreshBuffer))
            return _protector.Unprotect(connection.EncryptedAccessToken);

        var provider = ResolveProvider(connection.Provider);
        var refreshToken = _protector.Unprotect(connection.EncryptedRefreshToken);

        try
        {
            var refreshed = await provider.RefreshAccessTokenAsync(refreshToken, cancellationToken);
            connection.UpdateTokens(_protector.Protect(refreshed.AccessToken), _protector.Protect(refreshed.RefreshToken), refreshed.ExpiresAtUtc);
            await _db.SaveChangesAsync(cancellationToken);
            return refreshed.AccessToken;
        }
        catch (Exception ex)
        {
            RecordFailure(connection, ex);
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogError(ex, "Calendar token refresh failed for organizer {OrganizerId}.", organizerId);
            return null;
        }
    }

    /// <summary>
    /// Classifies a Google failure into the right CalendarSyncStatus and records it - shared
    /// by GetValidAccessTokenAsync and SyncNowAsync so the two don't drift into inconsistent
    /// interpretations of the same error shapes. Never logs the exception itself (callers do
    /// that, since only they know the right log level/context) - just mutates the entity.
    /// </summary>
    private static void RecordFailure(CalendarConnection connection, Exception ex)
    {
        if (ex is TokenResponseException tokenEx && tokenEx.Error?.Error == "invalid_grant")
        {
            // The organizer revoked access, or the refresh token otherwise stopped being valid -
            // no amount of retrying fixes this, only a fresh Connect can.
            connection.MarkSyncFailed("Google access was revoked or expired. Please reconnect your calendar.", requiresReauthorization: true);
            return;
        }

        if (ex is Google.GoogleApiException apiEx && apiEx.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            connection.MarkSyncFailed(
                $"The calendar \"{connection.ExternalCalendarName}\" could not be found. It may have been deleted - choose a different calendar.",
                requiresReauthorization: false, calendarNotFound: true);
            return;
        }

        // Transient (network, rate limit, outage) - leave everything else as-is beyond
        // recording the error, so the next sweep/request just tries again.
        connection.MarkSyncFailed(ex.Message, requiresReauthorization: false);
    }

    private async Task<CalendarConnectionDto> ToDtoAsync(CalendarConnection connection, CancellationToken cancellationToken)
    {
        var syncedCount = await _db.CalendarSyncedEvents.AsNoTracking()
            .CountAsync(e => e.CalendarConnectionId == connection.Id, cancellationToken);

        return new CalendarConnectionDto(
            connection.Id,
            connection.Provider.ToString(),
            connection.ExternalAccountEmail,
            connection.ExternalCalendarId,
            connection.ExternalCalendarName,
            connection.Status.ToString(),
            ToHealthLabel(connection.Status),
            connection.LastSyncError,
            connection.LastSuccessfulSyncAtUtc,
            connection.LastFailedSyncAtUtc,
            connection.ImportBusyEvents,
            connection.ExportBookings,
            connection.AutoDeleteCancelledBookings,
            connection.AutoUpdateRescheduledBookings,
            connection.DefaultReminderMinutes,
            connection.EventVisibility,
            connection.EventTitleFormat,
            syncedCount);
    }

    private static string ToHealthLabel(CalendarSyncStatus status) => status switch
    {
        CalendarSyncStatus.Connected => "Connected",
        CalendarSyncStatus.ReauthorizationRequired => "Needs Reauthentication",
        CalendarSyncStatus.CalendarNotFound => "Calendar Missing",
        CalendarSyncStatus.Error => "Synchronization Failed",
        _ => status.ToString()
    };

    private ICalendarProvider ResolveProvider(CalendarProviderType providerType) =>
        _providers.FirstOrDefault(p => p.ProviderType == providerType)
            ?? throw new InvalidOperationException($"No ICalendarProvider registered for {providerType}.");
}
