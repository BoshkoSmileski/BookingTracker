using BookingTracker.Application.Calendar.Dtos;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace BookingTracker.Infrastructure.Calendar;

public class CalendarSyncService : ICalendarSyncService
{
    /// <summary>
    /// Short enough that a real calendar change is never stale for long, long enough to absorb
    /// the burst of near-identical requests one visitor's date-picker interaction produces
    /// (React re-renders, month navigation hitting the same range twice, etc).
    /// </summary>
    private static readonly TimeSpan BusyIntervalCacheDuration = TimeSpan.FromSeconds(60);

    private readonly IBookingTrackerDbContext _db;
    private readonly ICalendarConnectionService _connectionService;
    private readonly IEnumerable<ICalendarProvider> _providers;
    private readonly IFrontendLinkBuilder _linkBuilder;
    private readonly IMemoryCache _cache;
    private readonly ILogger<CalendarSyncService> _logger;

    public CalendarSyncService(
        IBookingTrackerDbContext db,
        ICalendarConnectionService connectionService,
        IEnumerable<ICalendarProvider> providers,
        IFrontendLinkBuilder linkBuilder,
        IMemoryCache cache,
        ILogger<CalendarSyncService> logger)
    {
        _db = db;
        _connectionService = connectionService;
        _providers = providers;
        _linkBuilder = linkBuilder;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<CalendarBusyIntervalDto>> GetBusyIntervalsAsync(
        Guid organizerId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        // Cache key intentionally coarse (minute-level) - the public booking page can otherwise
        // trigger several near-simultaneous identical requests for the same organizer/range.
        var cacheKey = $"calendar-busy:{organizerId}:{fromUtc:yyyyMMddHHmm}:{toUtc:yyyyMMddHHmm}";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<CalendarBusyIntervalDto>? cached) && cached is not null)
        {
            _logger.LogInformation("Calendar busy-interval cache HIT for organizer {OrganizerId} ({FromUtc:o}..{ToUtc:o}).", organizerId, fromUtc, toUtc);
            return cached;
        }

        try
        {
            var connection = await _db.CalendarConnections.AsNoTracking()
                .FirstOrDefaultAsync(c => c.OrganizerId == organizerId && c.ImportBusyEvents, cancellationToken);
            if (connection is null) return [];

            // GetValidAccessTokenAsync already logs and marks Status on failure - null here
            // just means "couldn't get a token this time," not a new error to report.
            var accessToken = await _connectionService.GetValidAccessTokenAsync(organizerId, cancellationToken);
            if (accessToken is null) return [];

            var provider = ResolveProvider(connection.Provider);
            var result = await provider.GetBusyIntervalsAsync(accessToken, connection.ExternalCalendarId, fromUtc, toUtc, cancellationToken);

            _cache.Set(cacheKey, result, BusyIntervalCacheDuration);
            return result;
        }
        catch (Exception ex)
        {
            // Fail open: the public booking page must keep showing slots even if Google is
            // having an outage - worst case guests can double-book against an unreachable
            // calendar, which is far better than the booking page going blank.
            _logger.LogError(ex, "Failed to fetch busy calendar intervals for organizer {OrganizerId}.", organizerId);
            return [];
        }
    }

    public async Task SyncBookingCreatedAsync(Guid bookingSessionId, CancellationToken cancellationToken)
    {
        var context = await LoadSyncContextAsync(bookingSessionId, cancellationToken);
        if (context is null)
        {
            _logger.LogInformation("SyncBookingCreatedAsync: session {SessionId} has no date/time or page/organizer yet - skipping.", bookingSessionId);
            return;
        }

        var (session, page, organizer, timeZoneId, connection) = context.Value;
        if (connection is null)
        {
            _logger.LogInformation("SyncBookingCreatedAsync: organizer {OrganizerId} has no calendar connection - skipping.", organizer.Id);
            return;
        }
        if (!connection.ExportBookings)
        {
            _logger.LogInformation("SyncBookingCreatedAsync: organizer {OrganizerId} has ExportBookings disabled - skipping.", organizer.Id);
            return;
        }

        var accessToken = await _connectionService.GetValidAccessTokenAsync(organizer.Id, cancellationToken);
        if (accessToken is null)
        {
            _logger.LogWarning("SyncBookingCreatedAsync: could not obtain a valid access token for organizer {OrganizerId} - skipping.", organizer.Id);
            return;
        }

        var provider = ResolveProvider(connection.Provider);
        var details = BuildEventDetails(session, page, organizer, timeZoneId, connection);
        // The page's meeting setting is stated on every sync, including when it
        // is None. "No Meet link appeared" and "no Meet link was ever asked for"
        // look identical from the outside and have completely different fixes -
        // the first is a Google problem, the second is one checkbox on the
        // booking page - and without this line the logs cannot tell them apart.
        _logger.LogInformation(
            "SyncBookingCreatedAsync: creating Google event for session {SessionId} on calendar {CalendarId} ({StartUtc:o} - {EndUtc:o}); booking page {BookingPageId} has MeetingProvider={MeetingProvider}, so requestConference={RequestConference}.",
            session.Id, connection.ExternalCalendarId, details.StartUtc, details.EndUtc,
            page.Id, page.MeetingProvider, details.RequestConference);

        var result = await provider.CreateEventAsync(accessToken, connection.ExternalCalendarId, details, cancellationToken);
        _logger.LogInformation(
            "SyncBookingCreatedAsync: Google returned event id {ExternalEventId} for session {SessionId} (meetingLink={HasMeetingLink}).",
            result.ExternalEventId, session.Id, result.MeetingUrl is not null);

        _db.CalendarSyncedEvents.Add(CalendarSyncedEvent.Create(session.Id, connection.Id, result.ExternalEventId));
        RecordMeetingLink(session, page, result);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task SyncBookingRescheduledAsync(Guid bookingSessionId, CancellationToken cancellationToken)
    {
        var context = await LoadSyncContextAsync(bookingSessionId, cancellationToken);
        if (context is null)
        {
            _logger.LogInformation("SyncBookingRescheduledAsync: session {SessionId} has no date/time or page/organizer yet - skipping.", bookingSessionId);
            return;
        }

        var (session, page, organizer, timeZoneId, connection) = context.Value;
        if (connection is null || !connection.ExportBookings)
        {
            _logger.LogInformation("SyncBookingRescheduledAsync: organizer {OrganizerId} has no connection or ExportBookings disabled - skipping.", organizer.Id);
            return;
        }
        if (!connection.AutoUpdateRescheduledBookings)
        {
            _logger.LogInformation("SyncBookingRescheduledAsync: organizer {OrganizerId} has AutoUpdateRescheduledBookings disabled - leaving the calendar event as-is.", organizer.Id);
            return;
        }

        var accessToken = await _connectionService.GetValidAccessTokenAsync(organizer.Id, cancellationToken);
        if (accessToken is null)
        {
            _logger.LogWarning("SyncBookingRescheduledAsync: could not obtain a valid access token for organizer {OrganizerId} - skipping.", organizer.Id);
            return;
        }

        var provider = ResolveProvider(connection.Provider);
        var details = BuildEventDetails(session, page, organizer, timeZoneId, connection);

        var syncedEvent = await _db.CalendarSyncedEvents.FirstOrDefaultAsync(e => e.BookingSessionId == session.Id, cancellationToken);
        if (syncedEvent is null)
        {
            // The booking predates the calendar connection (or the first sync attempt
            // failed) - there's nothing to update, so create it now instead.
            _logger.LogInformation("SyncBookingRescheduledAsync: no existing synced event for session {SessionId} - creating one instead of updating.", session.Id);
            var result = await provider.CreateEventAsync(accessToken, connection.ExternalCalendarId, details, cancellationToken);
            _db.CalendarSyncedEvents.Add(CalendarSyncedEvent.Create(session.Id, connection.Id, result.ExternalEventId));
            // This booking never had an event, so it never had a meeting either -
            // creating one now is the first time a link exists for it.
            RecordMeetingLink(session, page, result);
        }
        else
        {
            // Update the existing event in place - never delete and recreate for a
            // reschedule. The event keeps its conference (see
            // GoogleCalendarProvider.UpdateEventAsync), so the session's stored
            // MeetingUrl stays correct and is deliberately not touched here.
            _logger.LogInformation(
                "SyncBookingRescheduledAsync: updating Google event {ExternalEventId} for session {SessionId} to {StartUtc:o} - {EndUtc:o}.",
                syncedEvent.ExternalEventId, session.Id, details.StartUtc, details.EndUtc);
            await provider.UpdateEventAsync(accessToken, connection.ExternalCalendarId, syncedEvent.ExternalEventId, details, cancellationToken);
            syncedEvent.MarkUpdated();
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task SyncBookingCancelledAsync(Guid bookingSessionId, CancellationToken cancellationToken)
    {
        var syncedEvent = await _db.CalendarSyncedEvents.FirstOrDefaultAsync(e => e.BookingSessionId == bookingSessionId, cancellationToken);
        if (syncedEvent is null)
        {
            _logger.LogInformation("SyncBookingCancelledAsync: no synced calendar event for session {SessionId} - nothing to remove.", bookingSessionId);
            return;
        }

        var connection = await _db.CalendarConnections.FirstOrDefaultAsync(c => c.Id == syncedEvent.CalendarConnectionId, cancellationToken);
        if (connection is null)
        {
            // The calendar was disconnected since this event was created - nothing left to delete against, just drop our tracking row.
            _logger.LogInformation(
                "SyncBookingCancelledAsync: calendar connection for session {SessionId}'s event no longer exists - dropping tracking row only.", bookingSessionId);
            _db.CalendarSyncedEvents.Remove(syncedEvent);
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (!connection.AutoDeleteCancelledBookings)
        {
            // The organizer wants cancelled bookings to stay visible on their calendar - leave
            // both the Google event and our tracking row alone (the row still accurately
            // describes "this session maps to that still-existing external event").
            _logger.LogInformation(
                "SyncBookingCancelledAsync: organizer {OrganizerId} has AutoDeleteCancelledBookings disabled - leaving Google event {ExternalEventId} in place.",
                connection.OrganizerId, syncedEvent.ExternalEventId);
            return;
        }

        var accessToken = await _connectionService.GetValidAccessTokenAsync(connection.OrganizerId, cancellationToken);
        if (accessToken is not null)
        {
            var provider = ResolveProvider(connection.Provider);
            _logger.LogInformation(
                "SyncBookingCancelledAsync: deleting Google event {ExternalEventId} for session {SessionId}.", syncedEvent.ExternalEventId, bookingSessionId);
            await provider.DeleteEventAsync(accessToken, connection.ExternalCalendarId, syncedEvent.ExternalEventId, cancellationToken);
        }
        else
        {
            _logger.LogWarning(
                "SyncBookingCancelledAsync: could not obtain a valid access token for organizer {OrganizerId} - Google event {ExternalEventId} was NOT deleted, only the local tracking row.",
                connection.OrganizerId, syncedEvent.ExternalEventId);
        }

        _db.CalendarSyncedEvents.Remove(syncedEvent);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<(BookingSession Session, BookingPage Page, Organizer Organizer, string TimeZoneId, CalendarConnection? Connection)?> LoadSyncContextAsync(
        Guid bookingSessionId, CancellationToken cancellationToken)
    {
        var session = await _db.BookingSessions.FirstOrDefaultAsync(s => s.Id == bookingSessionId, cancellationToken);
        if (session is null || session.SelectedDate is null || session.SelectedTime is null) return null;

        var page = await _db.BookingPages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == session.BookingPageId, cancellationToken);
        if (page is null) return null;

        var organizer = await _db.Organizers.AsNoTracking().FirstOrDefaultAsync(o => o.Id == page.OrganizerId, cancellationToken);
        if (organizer is null) return null;

        var timeZoneId = await _db.WorkingSchedules.AsNoTracking()
            .Where(s => s.OrganizerId == organizer.Id)
            .Select(s => s.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken) ?? "UTC";

        var connection = await _db.CalendarConnections.FirstOrDefaultAsync(c => c.OrganizerId == organizer.Id, cancellationToken);

        return (session, page, organizer, timeZoneId, connection);
    }

    private CalendarEventDetails BuildEventDetails(
        BookingSession session, BookingPage page, Organizer organizer, string timeZoneId, CalendarConnection connection)
    {
        var timeZone = ResolveTimeZone(timeZoneId);
        var localStart = DateTime.SpecifyKind(session.SelectedDate!.Value.ToDateTime(session.SelectedTime!.Value), DateTimeKind.Unspecified);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone);
        var endUtc = startUtc.AddMinutes(page.DurationMinutes);

        var description = string.IsNullOrWhiteSpace(page.Description)
            ? $"Booking reference: {session.BookingReference}"
            : $"{page.Description}\n\nBooking reference: {session.BookingReference}";

        var guestName = session.Name ?? "Guest";
        var title = FormatEventTitle(connection.EventTitleFormat, page.Title, guestName);

        // Only Submitted bookings ever have a PublicToken (set at Submit) - true here since
        // this method only runs for sessions that have already gone through Submit.
        var managementUrl = string.IsNullOrEmpty(session.PublicToken) ? null : _linkBuilder.BuildManageBookingUrl(session.PublicToken);

        return new CalendarEventDetails(
            Title: title,
            Description: description,
            StartUtc: startUtc,
            EndUtc: endUtc,
            TimeZoneId: timeZoneId,
            OrganizerEmail: organizer.Email,
            GuestName: guestName,
            GuestEmail: session.Email ?? string.Empty,
            BookingReference: session.BookingReference ?? string.Empty,
            Location: null,
            BookingManagementUrl: managementUrl,
            ReminderMinutes: connection.DefaultReminderMinutes,
            Visibility: connection.EventVisibility,
            // The booking page decides whether this booking is an online
            // meeting; the provider decides what creating one means. An
            // In person page asks for nothing, so its events are created
            // exactly as they were before Meet support existed.
            RequestConference: page.MeetingProvider != MeetingProviderType.None);
    }

    /// <summary>
    /// Stores the join URL the provider generated, as an event on the booking
    /// session rather than a bare column write - so /rebuild reconstructs it
    /// from the log like every other fact about a booking.
    ///
    /// Silent no-op when no URL came back. That is the fail-open half of this
    /// feature: an organizer with no calendar connection, a Google outage, a
    /// Workspace policy that forbids conferences, or a conference Google is
    /// still creating all end here, and all of them mean "this booking has no
    /// meeting link" - never "this booking failed". The emails and the ICS
    /// attachment simply omit their Join sections.
    /// </summary>
    private void RecordMeetingLink(BookingSession session, BookingPage page, CalendarEventResult result)
    {
        if (result.MeetingUrl is null || page.MeetingProvider == MeetingProviderType.None) return;

        // Null when the session already holds this exact link - one booking is
        // one meeting, so a repeat sync must not append a second event.
        var @event = session.AssignMeetingLink(page.MeetingProvider, result.MeetingUrl);
        if (@event is null) return;

        _db.BookingSessionEvents.Add(@event);
        _logger.LogInformation(
            "Recorded {MeetingProvider} meeting link for session {SessionId}.", page.MeetingProvider, session.Id);
    }

    private static string FormatEventTitle(string format, string serviceName, string guestName) =>
        format.Replace("{Service Name}", serviceName).Replace("{Guest Name}", guestName);

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private ICalendarProvider ResolveProvider(CalendarProviderType providerType) =>
        _providers.FirstOrDefault(p => p.ProviderType == providerType)
            ?? throw new InvalidOperationException($"No ICalendarProvider registered for {providerType}.");
}
