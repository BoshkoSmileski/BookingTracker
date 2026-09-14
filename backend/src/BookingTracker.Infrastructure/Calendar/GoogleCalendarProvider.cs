using BookingTracker.Application.Calendar.Dtos;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Domain.Enums;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BookingTracker.Infrastructure.Calendar;

/// <summary>
/// The only place in this codebase that references the Google.Apis SDK -
/// Application never sees a Google type, only ICalendarProvider's own DTOs.
/// Tokens are handled as opaque strings here; encrypting them at rest is
/// CalendarConnectionService's job, not this class's. Every call out to
/// Google is logged (request + response/error) - this is the sole place API
/// failures can be diagnosed from, since everything above here only sees the
/// DTOs/exceptions this class produces.
/// </summary>
public class GoogleCalendarProvider : ICalendarProvider
{
    private const string ApplicationName = "BookingTracker";

    private readonly GoogleCalendarSettings _settings;
    private readonly ILogger<GoogleCalendarProvider> _logger;

    public GoogleCalendarProvider(IOptions<GoogleCalendarSettings> settings, ILogger<GoogleCalendarProvider> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public CalendarProviderType ProviderType => CalendarProviderType.Google;

    public string BuildAuthorizationUrl(string state)
    {
        var flow = CreateFlow();
        var request = (GoogleAuthorizationCodeRequestUrl)flow.CreateAuthorizationCodeRequest(_settings.RedirectUri);
        request.State = state;
        // offline + forcing the consent screen guarantees a refresh token every time -
        // without this, Google only issues one on the organizer's very first-ever consent.
        request.AccessType = "offline";
        request.Prompt = "consent";
        return request.Build().ToString();
    }

    public async Task<CalendarOAuthResult> ExchangeAuthorizationCodeAsync(string authorizationCode, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Google request: ExchangeCodeForToken (redirectUri={RedirectUri}).", _settings.RedirectUri);
        var flow = CreateFlow();
        TokenResponse token;
        try
        {
            token = await flow.ExchangeCodeForTokenAsync("organizer", authorizationCode, _settings.RedirectUri, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google response: ExchangeCodeForToken FAILED.");
            throw;
        }
        _logger.LogInformation("Google response: ExchangeCodeForToken OK (expiresInSeconds={ExpiresInSeconds}, hasRefreshToken={HasRefreshToken}).",
            token.ExpiresInSeconds, !string.IsNullOrEmpty(token.RefreshToken));

        if (string.IsNullOrEmpty(token.RefreshToken))
        {
            throw new InvalidOperationException(
                "Google did not return a refresh token. This usually means consent was already granted previously without offline access - revoke access at https://myaccount.google.com/permissions and try connecting again.");
        }

        var accountEmail = await GetPrimaryCalendarIdAsync(token.AccessToken, cancellationToken);
        return new CalendarOAuthResult(token.AccessToken, token.RefreshToken, ExpiresAtUtc(token), accountEmail);
    }

    public async Task<CalendarOAuthResult> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Google request: RefreshToken.");
        var flow = CreateFlow();
        TokenResponse token;
        try
        {
            token = await flow.RefreshTokenAsync("organizer", refreshToken, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google response: RefreshToken FAILED.");
            throw;
        }
        _logger.LogInformation("Google response: RefreshToken OK (expiresInSeconds={ExpiresInSeconds}).", token.ExpiresInSeconds);

        // Google's refresh response normally omits RefreshToken (the original stays valid) -
        // the caller already holds the account email from the stored connection, so it's not
        // re-derived here to avoid an extra API call on every refresh.
        return new CalendarOAuthResult(token.AccessToken, token.RefreshToken ?? refreshToken, ExpiresAtUtc(token), AccountEmail: string.Empty);
    }

    public async Task<IReadOnlyList<ExternalCalendarDto>> ListCalendarsAsync(string accessToken, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Google request: CalendarList.List.");
        using var service = CreateService(accessToken);
        try
        {
            var result = await WithRetryAsync(
                () => service.CalendarList.List().ExecuteAsync(cancellationToken), "CalendarList.List", cancellationToken);
            _logger.LogInformation("Google response: CalendarList.List OK ({Count} calendar(s)).", result.Items?.Count ?? 0);
            return (result.Items ?? [])
                .Select(c => new ExternalCalendarDto(c.Id, c.Summary, c.Primary ?? false))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google response: CalendarList.List FAILED.");
            throw;
        }
    }

    public async Task<IReadOnlyList<CalendarBusyIntervalDto>> GetBusyIntervalsAsync(
        string accessToken, string calendarId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Google request: Freebusy.Query (calendarId={CalendarId}, {FromUtc:o}..{ToUtc:o}).", calendarId, fromUtc, toUtc);
        using var service = CreateService(accessToken);
        var request = new FreeBusyRequest
        {
            TimeMinDateTimeOffset = new DateTimeOffset(fromUtc, TimeSpan.Zero),
            TimeMaxDateTimeOffset = new DateTimeOffset(toUtc, TimeSpan.Zero),
            Items = [new FreeBusyRequestItem { Id = calendarId }]
        };

        FreeBusyResponse response;
        try
        {
            response = await WithRetryAsync(
                () => service.Freebusy.Query(request).ExecuteAsync(cancellationToken), "Freebusy.Query", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google response: Freebusy.Query FAILED for calendar {CalendarId}.", calendarId);
            throw;
        }

        if (!response.Calendars.TryGetValue(calendarId, out var calendar))
        {
            _logger.LogWarning("Google response: Freebusy.Query OK but returned no entry for calendar {CalendarId} (available keys: {Keys}).",
                calendarId, string.Join(", ", response.Calendars.Keys));
            return [];
        }
        if (calendar.Busy is null)
        {
            _logger.LogInformation("Google response: Freebusy.Query OK, calendar {CalendarId} has 0 busy interval(s).", calendarId);
            return [];
        }

        var busy = calendar.Busy
            .Where(p => p.StartDateTimeOffset.HasValue && p.EndDateTimeOffset.HasValue)
            .Select(p => new CalendarBusyIntervalDto(p.StartDateTimeOffset!.Value.UtcDateTime, p.EndDateTimeOffset!.Value.UtcDateTime))
            .ToList();
        _logger.LogInformation("Google response: Freebusy.Query OK, calendar {CalendarId} has {Count} busy interval(s): {Intervals}.",
            calendarId, busy.Count, string.Join("; ", busy.Select(b => $"{b.StartUtc:o}-{b.EndUtc:o}")));
        return busy;
    }

    public async Task<CalendarEventResult> CreateEventAsync(string accessToken, string calendarId, CalendarEventDetails details, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Google request: Events.Insert (calendarId={CalendarId}, title=\"{Title}\", {StartUtc:o}..{EndUtc:o}, requestConference={RequestConference}).",
            calendarId, details.Title, details.StartUtc, details.EndUtc, details.RequestConference);
        using var service = CreateService(accessToken);
        var googleEvent = ToGoogleEvent(details);

        if (details.RequestConference)
        {
            AttachConferenceRequest(googleEvent, details.BookingReference);
        }

        try
        {
            var insert = service.Events.Insert(googleEvent, calendarId);
            // Without this, Google ignores conferenceData in the request body
            // entirely and the event is created with no Meet link at all - the
            // single most likely way for this feature to silently do nothing.
            // Only set when a conference was actually asked for, so an
            // in-person booking's insert is byte-for-byte the request it was
            // before this feature existed.
            if (details.RequestConference) insert.ConferenceDataVersion = 1;

            var created = await insert.ExecuteAsync(cancellationToken);
            var meetingUrl = ExtractMeetingUrl(created);

            // The conference half of the response is logged in full, not just as
            // a boolean. "hasMeeting=False" alone cannot distinguish the several
            // very different reasons Google declines to attach a Meet - a
            // pending createRequest, a solution the calendar cannot host, or a
            // request that never asked - and those need different fixes.
            _logger.LogInformation(
                "Google response: Events.Insert OK (eventId={EventId}, htmlLink={HtmlLink}, hasMeeting={HasMeeting}, conference={Conference}).",
                created.Id, created.HtmlLink, meetingUrl is not null, DescribeConference(created));

            if (details.RequestConference && meetingUrl is null)
            {
                // Not an error: Google can report a conference as still being
                // created, and some calendars (or Workspace policies) refuse to
                // host one. The booking is already made, so it degrades to a
                // booking without a join link rather than failing.
                _logger.LogWarning(
                    "Google response: Events.Insert OK but returned no conference join URL for event {EventId} (createRequest status={Status}). The booking has no meeting link.",
                    created.Id, created.ConferenceData?.CreateRequest?.Status?.StatusCode ?? "none");
            }

            return new CalendarEventResult(created.Id, meetingUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google response: Events.Insert FAILED for calendar {CalendarId}.", calendarId);
            throw;
        }
    }

    /// <summary>
    /// Asks Google to generate a Meet conference as part of creating the event.
    /// The link is never constructed here - Google mints it and returns it on
    /// the insert response, which is the only way to get a real, joinable Meet.
    ///
    /// RequestId is Google's idempotency key: repeating an insert with the same
    /// id returns the same conference instead of minting a second one. It is
    /// derived from the booking reference (unique per booking, stable for its
    /// whole life) rather than a fresh Guid, so a retried create for one booking
    /// cannot produce two meetings.
    /// </summary>
    private static void AttachConferenceRequest(Event googleEvent, string bookingReference)
    {
        googleEvent.ConferenceData = new ConferenceData
        {
            CreateRequest = new CreateConferenceRequest
            {
                RequestId = $"bookingtracker-{bookingReference}",
                ConferenceSolutionKey = new ConferenceSolutionKey { Type = "hangoutsMeet" }
            }
        };
    }

    /// <summary>
    /// Everything Google said about the event's conference, as one log-safe
    /// string: the solution it used, the entry points it issued, and the
    /// create-request status. Contains no tokens - a Meet URL is a meeting
    /// address the guest is emailed anyway, not a credential.
    /// </summary>
    private static string DescribeConference(Event created)
    {
        var data = created.ConferenceData;
        if (data is null) return "none returned";

        var entryPoints = data.EntryPoints is null or []
            ? "no entry points"
            : string.Join(", ", data.EntryPoints.Select(e => $"{e.EntryPointType}={e.Uri}"));

        return $"solution={data.ConferenceSolution?.Key?.Type ?? "none"}, " +
               $"conferenceId={data.ConferenceId ?? "none"}, " +
               $"createRequestStatus={data.CreateRequest?.Status?.StatusCode ?? "none"}, " +
               $"hangoutLink={created.HangoutLink ?? "none"}, {entryPoints}";
    }

    /// <summary>
    /// HangoutLink is the join URL for a Meet conference and is what every
    /// Google client shows. The video entry point is the general form of the
    /// same thing and is checked as a fallback, so this keeps working if a
    /// future conference solution populates only the structured field.
    /// </summary>
    private static string? ExtractMeetingUrl(Event created)
    {
        if (!string.IsNullOrWhiteSpace(created.HangoutLink)) return created.HangoutLink;

        return created.ConferenceData?.EntryPoints?
            .FirstOrDefault(e => e.EntryPointType == "video" && !string.IsNullOrWhiteSpace(e.Uri))?.Uri;
    }

    public async Task UpdateEventAsync(string accessToken, string calendarId, string externalEventId, CalendarEventDetails details, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Google request: Events.Update (calendarId={CalendarId}, eventId={EventId}, {StartUtc:o}..{EndUtc:o}).",
            calendarId, externalEventId, details.StartUtc, details.EndUtc);
        using var service = CreateService(accessToken);
        var googleEvent = ToGoogleEvent(details);
        try
        {
            // ConferenceDataVersion is deliberately LEFT UNSET (0) here, and
            // that is what preserves an existing Meet link across a reschedule.
            //
            // At version 0 Google ignores conference data in the request body
            // and keeps whatever the event already has. At version 1 it would
            // honour the body - and this body carries no conferenceData,
            // because ToGoogleEvent builds the event from the booking's details
            // and the join URL is not one of them - so setting it to 1 would
            // strip the conference off every rescheduled booking, breaking the
            // link guests were already sent. One booking is one meeting: a
            // reschedule moves the event, it never re-creates the conference.
            var updated = await service.Events.Update(googleEvent, calendarId, externalEventId).ExecuteAsync(cancellationToken);
            _logger.LogInformation(
                "Google response: Events.Update OK (eventId={EventId}, status={Status}, conferencePreserved={ConferencePreserved}).",
                updated.Id, updated.Status, !string.IsNullOrWhiteSpace(updated.HangoutLink));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google response: Events.Update FAILED for calendar {CalendarId}, event {EventId}.", calendarId, externalEventId);
            throw;
        }
    }

    public async Task DeleteEventAsync(string accessToken, string calendarId, string externalEventId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Google request: Events.Delete (calendarId={CalendarId}, eventId={EventId}).", calendarId, externalEventId);
        using var service = CreateService(accessToken);
        try
        {
            await service.Events.Delete(calendarId, externalEventId).ExecuteAsync(cancellationToken);
            _logger.LogInformation("Google response: Events.Delete OK (eventId={EventId}).", externalEventId);
        }
        catch (Google.GoogleApiException ex) when (
            ex.HttpStatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone)
        {
            // Already gone (organizer deleted it manually, or we already deleted it) -
            // the goal state (event doesn't exist) is already achieved, so this is a success.
            _logger.LogInformation("Google response: Events.Delete - event {EventId} was already gone (treated as success).", externalEventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google response: Events.Delete FAILED for calendar {CalendarId}, event {EventId}.", calendarId, externalEventId);
            throw;
        }
    }

    private async Task<string> GetPrimaryCalendarIdAsync(string accessToken, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Google request: CalendarList.Get(\"primary\").");
        using var service = CreateService(accessToken);
        try
        {
            var primary = await WithRetryAsync(
                () => service.CalendarList.Get("primary").ExecuteAsync(cancellationToken), "CalendarList.Get(primary)", cancellationToken);
            _logger.LogInformation("Google response: CalendarList.Get(\"primary\") OK (id={CalendarId}).", primary.Id);
            // For Google accounts, the primary calendar's id IS the account's email address.
            return primary.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Google response: CalendarList.Get(\"primary\") FAILED.");
            throw;
        }
    }

    private GoogleAuthorizationCodeFlow CreateFlow() => new(new GoogleAuthorizationCodeFlow.Initializer
    {
        ClientSecrets = new ClientSecrets { ClientId = _settings.ClientId, ClientSecret = _settings.ClientSecret },
        Scopes = [CalendarService.Scope.Calendar]
    });

    private static CalendarService CreateService(string accessToken) => new(new BaseClientService.Initializer
    {
        HttpClientInitializer = GoogleCredential.FromAccessToken(accessToken),
        ApplicationName = ApplicationName
    });

    private static Event ToGoogleEvent(CalendarEventDetails details)
    {
        var description = string.IsNullOrEmpty(details.BookingManagementUrl)
            ? details.Description
            : $"{details.Description}\n\nManage this booking: {details.BookingManagementUrl}";

        return new Event
        {
            Summary = details.Title,
            Description = description,
            Location = details.Location,
            Visibility = details.Visibility,
            Start = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(details.StartUtc, TimeSpan.Zero), TimeZone = details.TimeZoneId },
            End = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(details.EndUtc, TimeSpan.Zero), TimeZone = details.TimeZoneId },
            Attendees =
            [
                new EventAttendee { Email = details.OrganizerEmail, ResponseStatus = "accepted" },
                new EventAttendee { Email = details.GuestEmail, DisplayName = details.GuestName }
            ],
            ExtendedProperties = new Event.ExtendedPropertiesData
            {
                Private__ = new Dictionary<string, string> { ["bookingReference"] = details.BookingReference }
            },
            Reminders = details.ReminderMinutes is { } minutes
                ? new Event.RemindersData
                {
                    UseDefault = false,
                    Overrides = [new EventReminder { Method = "popup", Minutes = minutes }]
                }
                : new Event.RemindersData { UseDefault = true }
        };
    }

    private static DateTime ExpiresAtUtc(TokenResponse token) =>
        (token.IssuedUtc == default ? DateTime.UtcNow : token.IssuedUtc).AddSeconds(token.ExpiresInSeconds ?? 3600);

    /// <summary>
    /// Retries idempotent READ calls (list/query) on transient failures - network blips, rate
    /// limits (429), and server-side outages (5xx). Deliberately NOT used for
    /// Insert/Update/Delete: those aren't safely retryable without an idempotency key Google's
    /// API doesn't provide, so a write failure is surfaced once and left to the caller (the
    /// booking command handlers already treat calendar sync as best-effort) rather than risking
    /// a duplicate write on retry.
    /// </summary>
    private async Task<T> WithRetryAsync<T>(Func<Task<T>> action, string operationName, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        var delay = TimeSpan.FromSeconds(1);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (attempt < maxAttempts && IsTransient(ex))
            {
                _logger.LogWarning(ex, "Google request {Operation} failed on attempt {Attempt}/{MaxAttempts} - retrying in {DelaySeconds}s.",
                    operationName, attempt, maxAttempts, delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
                delay += delay; // 1s, 2s
            }
        }
    }

    private static bool IsTransient(Exception ex) => ex switch
    {
        Google.GoogleApiException gex => (int)gex.HttpStatusCode >= 500 || (int)gex.HttpStatusCode == 429,
        HttpRequestException => true,
        TaskCanceledException => true,
        _ => false
    };
}
