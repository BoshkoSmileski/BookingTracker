using BookingTracker.Application.Calendar.Commands.ConnectGoogleCalendar;
using BookingTracker.Application.Calendar.Commands.DisconnectCalendar;
using BookingTracker.Application.Calendar.Commands.SelectCalendar;
using BookingTracker.Application.Calendar.Commands.SyncNow;
using BookingTracker.Application.Calendar.Commands.UpdateCalendarEventSettings;
using BookingTracker.Application.Calendar.Commands.UpdateCalendarSyncSettings;
using BookingTracker.Application.Calendar.Dtos;
using BookingTracker.Application.Calendar.Queries.GetAvailableCalendars;
using BookingTracker.Application.Calendar.Queries.GetCalendarConnection;
using BookingTracker.Application.Calendar.Queries.GetGoogleAuthorizationUrl;
using BookingTracker.Application.Common.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

namespace BookingTracker.Api.Controllers;

/// <summary>
/// Organizer calendar integration (Google today; the ICalendarProvider
/// provider abstraction means Outlook/Apple slot in later without
/// touching this controller's shape). Every action requires an authenticated
/// organizer EXCEPT the OAuth callback, which Google's browser redirect hits
/// directly with no Authorization header - that one action instead validates
/// a signed, time-limited "state" value (see CreateState/TryDecodeState) that
/// encodes which organizer/page initiated the flow. This is the OAuth "state"
/// parameter's whole purpose: it IS the CSRF/tamper protection here, since a
/// forged or expired state fails to unprotect and the flow is rejected.
/// </summary>
[ApiController]
[Authorize]
[Route("api/calendar")]
public class CalendarController : ControllerBase
{
    private const string StateProtectorPurpose = "BookingTracker.CalendarOAuthState";
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUser;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IFrontendLinkBuilder _linkBuilder;
    private readonly ILogger<CalendarController> _logger;

    public CalendarController(
        IMediator mediator,
        ICurrentUserService currentUser,
        IDataProtectionProvider dataProtectionProvider,
        IFrontendLinkBuilder linkBuilder,
        ILogger<CalendarController> logger)
    {
        _mediator = mediator;
        _currentUser = currentUser;
        _dataProtectionProvider = dataProtectionProvider;
        _linkBuilder = linkBuilder;
        _logger = logger;
    }

    private Guid OrganizerId => _currentUser.OrganizerId
        ?? throw new InvalidOperationException("Authenticated request is missing the organizer id claim.");

    [HttpGet("connection")]
    public async Task<ActionResult<CalendarConnectionDto?>> GetConnection(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetCalendarConnectionQuery(OrganizerId), cancellationToken);
        return Ok(result);
    }

    /// <summary>Returns the URL to send the browser to for Google consent - the frontend does the actual redirect (window.location.href), since this call itself is an authenticated fetch and can't be the OAuth redirect target directly.</summary>
    [HttpGet("google/connect")]
    public async Task<ActionResult<AuthorizeUrlResponse>> GoogleConnect([FromQuery] Guid pageId, CancellationToken cancellationToken)
    {
        var state = CreateState(OrganizerId, pageId);
        var url = await _mediator.Send(new GetGoogleAuthorizationUrlQuery(state), cancellationToken);
        return Ok(new AuthorizeUrlResponse(url));
    }

    /// <summary>Google's redirect target - never called by the frontend directly, and never carries a bearer token.</summary>
    [HttpGet("google/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> GoogleCallback(
        [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Google Calendar callback received (hasCode={HasCode}, hasState={HasState}, error={Error}).",
            !string.IsNullOrEmpty(code), !string.IsNullOrEmpty(state), error ?? "(none)");

        if (string.IsNullOrEmpty(state))
        {
            _logger.LogWarning("Google Calendar callback received with no state parameter.");
            return Redirect(_linkBuilder.BuildCalendarSettingsUrl(Guid.Empty, "calendarError=invalid_state"));
        }

        var decoded = TryDecodeState(state);
        if (decoded is null)
        {
            _logger.LogWarning("Google Calendar callback received an invalid or expired state parameter.");
            return Redirect(_linkBuilder.BuildCalendarSettingsUrl(Guid.Empty, "calendarError=invalid_state"));
        }

        var (organizerId, pageId) = decoded.Value;

        if (!string.IsNullOrEmpty(error))
        {
            // The organizer clicked "Cancel"/"Deny" on Google's consent screen.
            return Redirect(_linkBuilder.BuildCalendarSettingsUrl(pageId, "calendarError=access_denied"));
        }

        if (string.IsNullOrEmpty(code))
        {
            return Redirect(_linkBuilder.BuildCalendarSettingsUrl(pageId, "calendarError=missing_code"));
        }

        try
        {
            var result = await _mediator.Send(new ConnectGoogleCalendarCommand(organizerId, code), cancellationToken);
            _logger.LogInformation(
                "Google Calendar connected for organizer {OrganizerId}: account={AccountEmail}, calendar={CalendarName} ({CalendarId}).",
                organizerId, result.ExternalAccountEmail, result.ExternalCalendarName, result.ExternalCalendarId);
            return Redirect(_linkBuilder.BuildCalendarSettingsUrl(pageId, "connected=true"));
        }
        catch (Google.GoogleApiException ex) when (ex.Error?.Errors?.Any(e => e.Reason == "accessNotConfigured") == true)
        {
            // The OAuth handshake itself succeeded (we already have a valid access token) -
            // this specifically means the Calendar API hasn't been enabled on the organizer's
            // Google Cloud project yet. Surfacing this distinctly saves a trip through the
            // server logs to diagnose what is, in practice, the single most common setup gap.
            _logger.LogError(ex, "Google Calendar API is not enabled for organizer {OrganizerId}'s Google Cloud project.", organizerId);
            return Redirect(_linkBuilder.BuildCalendarSettingsUrl(pageId, "calendarError=api_not_enabled"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete Google Calendar connection for organizer {OrganizerId}.", organizerId);
            return Redirect(_linkBuilder.BuildCalendarSettingsUrl(pageId, "calendarError=connect_failed"));
        }
    }

    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect(CancellationToken cancellationToken)
    {
        await _mediator.Send(new DisconnectCalendarCommand(OrganizerId), cancellationToken);
        return NoContent();
    }

    [HttpGet("calendars")]
    public async Task<ActionResult<IReadOnlyList<ExternalCalendarDto>>> GetCalendars(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAvailableCalendarsQuery(OrganizerId), cancellationToken);
        return Ok(result);
    }

    [HttpPost("select-calendar")]
    public async Task<ActionResult<CalendarConnectionDto>> SelectCalendar(SelectCalendarRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new SelectCalendarCommand(OrganizerId, request.ExternalCalendarId, request.ExternalCalendarName), cancellationToken);
        return Ok(result);
    }

    [HttpPost("sync-settings")]
    public async Task<ActionResult<CalendarConnectionDto>> UpdateSyncSettings(SyncSettingsRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateCalendarSyncSettingsCommand(OrganizerId, request.ImportBusyEvents, request.ExportBookings), cancellationToken);
        return Ok(result);
    }

    [HttpPost("event-settings")]
    public async Task<ActionResult<CalendarConnectionDto>> UpdateEventSettings(EventSettingsRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateCalendarEventSettingsCommand(
                OrganizerId, request.EventTitleFormat, request.AutoDeleteCancelledBookings,
                request.AutoUpdateRescheduledBookings, request.DefaultReminderMinutes, request.EventVisibility),
            cancellationToken);
        return Ok(result);
    }

    /// <summary>Manual "Sync Now" - refreshes the token if needed, verifies calendar access, pulls fresh busy intervals, and updates the last-sync timestamps. Never 500s: failures come back as a normal 200 with an updated Status/HealthStatus/LastSyncError on the DTO.</summary>
    [HttpPost("sync-now")]
    public async Task<ActionResult<CalendarConnectionDto>> SyncNow(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SyncNowCommand(OrganizerId), cancellationToken);
        return Ok(result);
    }

    private string CreateState(Guid organizerId, Guid pageId) =>
        _dataProtectionProvider.CreateProtector(StateProtectorPurpose).ToTimeLimitedDataProtector()
            .Protect($"{organizerId}|{pageId}", StateLifetime);

    private (Guid OrganizerId, Guid PageId)? TryDecodeState(string state)
    {
        try
        {
            var payload = _dataProtectionProvider.CreateProtector(StateProtectorPurpose).ToTimeLimitedDataProtector().Unprotect(state);
            var parts = payload.Split('|');
            if (parts.Length != 2 || !Guid.TryParse(parts[0], out var organizerId) || !Guid.TryParse(parts[1], out var pageId))
                return null;

            return (organizerId, pageId);
        }
        catch
        {
            // Tampered, corrupted, or expired (data protection throws CryptographicException past the lifetime window) - all treated the same: reject.
            return null;
        }
    }
}

public record AuthorizeUrlResponse(string AuthorizationUrl);
public record SelectCalendarRequest(string ExternalCalendarId, string ExternalCalendarName);
public record SyncSettingsRequest(bool ImportBusyEvents, bool ExportBookings);
public record EventSettingsRequest(
    string EventTitleFormat, bool AutoDeleteCancelledBookings, bool AutoUpdateRescheduledBookings,
    int? DefaultReminderMinutes, string EventVisibility);
