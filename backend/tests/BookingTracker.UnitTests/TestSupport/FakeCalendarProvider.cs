using BookingTracker.Application.Calendar.Dtos;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Domain.Enums;

namespace BookingTracker.UnitTests.TestSupport;

/// <summary>
/// Stands in for GoogleCalendarProvider so CalendarSyncService can be driven
/// without Google. Hand-written rather than mocked, like every other double
/// here - what these tests need is to control one return value and record what
/// was asked for, which a mocking library would not make shorter.
///
/// It deliberately does NOT simulate Google's conference semantics; that is
/// GoogleCalendarProvider's own business and is asserted separately. What it
/// does model is the contract CalendarSyncService codes against: a create can
/// return a meeting URL or not, an update never produces one, and a create can
/// throw.
/// </summary>
public sealed class FakeCalendarProvider : ICalendarProvider
{
    public CalendarProviderType ProviderType => CalendarProviderType.Google;

    /// <summary>What CreateEventAsync hands back as the conference join URL. Null models "no conference was created".</summary>
    public string? MeetingUrlToReturn { get; set; }

    public string EventIdToReturn { get; set; } = "google-event-1";

    /// <summary>When set, CreateEventAsync throws it - the Google-is-unavailable case.</summary>
    public Exception? CreateEventException { get; set; }

    public List<CalendarEventDetails> CreatedEvents { get; } = [];
    public List<(string ExternalEventId, CalendarEventDetails Details)> UpdatedEvents { get; } = [];
    public List<string> DeletedEventIds { get; } = [];

    public Task<CalendarEventResult> CreateEventAsync(
        string accessToken, string calendarId, CalendarEventDetails details, CancellationToken cancellationToken)
    {
        if (CreateEventException is not null) throw CreateEventException;

        CreatedEvents.Add(details);
        // Only hands back a URL when one was actually asked for - modelling the
        // real provider, which never attaches a conference to an event that did
        // not request one.
        return Task.FromResult(new CalendarEventResult(
            EventIdToReturn, details.RequestConference ? MeetingUrlToReturn : null));
    }

    public Task UpdateEventAsync(
        string accessToken, string calendarId, string externalEventId, CalendarEventDetails details, CancellationToken cancellationToken)
    {
        UpdatedEvents.Add((externalEventId, details));
        return Task.CompletedTask;
    }

    public Task DeleteEventAsync(string accessToken, string calendarId, string externalEventId, CancellationToken cancellationToken)
    {
        DeletedEventIds.Add(externalEventId);
        return Task.CompletedTask;
    }

    // Not exercised by the sync-service tests - the OAuth and calendar-listing
    // half of the interface belongs to the connection service.
    public string BuildAuthorizationUrl(string state) => throw new NotSupportedException();

    public Task<CalendarOAuthResult> ExchangeAuthorizationCodeAsync(string authorizationCode, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<CalendarOAuthResult> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ExternalCalendarDto>> ListCalendarsAsync(string accessToken, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<CalendarBusyIntervalDto>> GetBusyIntervalsAsync(
        string accessToken, string calendarId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CalendarBusyIntervalDto>>([]);
}

/// <summary>
/// Supplies CalendarSyncService with an access token without any OAuth. Setting
/// <see cref="AccessToken"/> to null models the "token could not be obtained"
/// path (revoked consent, reauthorization required), which the service must
/// treat as skip-and-log rather than as an error.
/// </summary>
public sealed class FakeCalendarConnectionService : ICalendarConnectionService
{
    public string? AccessToken { get; set; } = "test-access-token";

    public Task<string?> GetValidAccessTokenAsync(Guid organizerId, CancellationToken cancellationToken) =>
        Task.FromResult(AccessToken);

    public Task<CalendarConnectionDto> ConnectAsync(
        Guid organizerId, CalendarProviderType provider, string authorizationCode, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task DisconnectAsync(Guid organizerId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<CalendarConnectionDto?> GetConnectionAsync(Guid organizerId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ExternalCalendarDto>> ListAvailableCalendarsAsync(Guid organizerId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<CalendarConnectionDto> SelectCalendarAsync(
        Guid organizerId, string externalCalendarId, string externalCalendarName, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<CalendarConnectionDto> UpdateSyncSettingsAsync(
        Guid organizerId, bool importBusyEvents, bool exportBookings, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<CalendarConnectionDto> UpdateEventSettingsAsync(
        Guid organizerId, string eventTitleFormat, bool autoDeleteCancelledBookings, bool autoUpdateRescheduledBookings,
        int? defaultReminderMinutes, string eventVisibility, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<CalendarConnectionDto> SyncNowAsync(Guid organizerId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
