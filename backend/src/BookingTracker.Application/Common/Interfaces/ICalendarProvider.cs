using BookingTracker.Application.Calendar.Dtos;
using BookingTracker.Domain.Enums;

namespace BookingTracker.Application.Common.Interfaces;

/// <summary>
/// One implementation per external calendar service (Google today; Outlook/
/// Apple later are just new classes behind this same interface - Application
/// never references a provider SDK type directly). Every method operates on
/// a single already-known access token; refreshing that token is
/// ICalendarConnectionService's job, not the provider's.
/// </summary>
public interface ICalendarProvider
{
    CalendarProviderType ProviderType { get; }

    /// <summary>Builds the URL to send the organizer's browser to for consent. <paramref name="state"/> round-trips through the provider unchanged.</summary>
    string BuildAuthorizationUrl(string state);

    Task<CalendarOAuthResult> ExchangeAuthorizationCodeAsync(string authorizationCode, CancellationToken cancellationToken);

    Task<CalendarOAuthResult> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExternalCalendarDto>> ListCalendarsAsync(string accessToken, CancellationToken cancellationToken);

    Task<IReadOnlyList<CalendarBusyIntervalDto>> GetBusyIntervalsAsync(
        string accessToken, string calendarId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);

    /// <returns>The new event's provider id (to store for later update/delete) and, when <see cref="CalendarEventDetails.RequestConference"/> asked for one, its video-conference join URL.</returns>
    Task<CalendarEventResult> CreateEventAsync(string accessToken, string calendarId, CalendarEventDetails details, CancellationToken cancellationToken);

    /// <summary>
    /// Updates an existing event in place. Implementations must PRESERVE any
    /// conference already attached to it: a rescheduled booking keeps the same
    /// meeting, so a guest who joins from the original invitation still lands
    /// in the right call.
    /// </summary>
    Task UpdateEventAsync(string accessToken, string calendarId, string externalEventId, CalendarEventDetails details, CancellationToken cancellationToken);

    Task DeleteEventAsync(string accessToken, string calendarId, string externalEventId, CancellationToken cancellationToken);
}
