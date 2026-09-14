namespace BookingTracker.Application.Calendar.Dtos;

/// <summary>
/// What a provider hands back after creating an event.
///
/// Replaced a bare <c>string</c> event id when Google Meet support landed: the
/// conference is created *by the same insert call* that creates the event, and
/// its join URL is returned exactly once, in that response. Making the caller
/// re-read the event to find it would be a second round trip for data it was
/// already given, so the create result carries both.
/// </summary>
/// <param name="ExternalEventId">The provider's id for the new event, stored for later update/delete.</param>
/// <param name="MeetingUrl">
/// The video-conference join URL, when one was requested (see
/// <see cref="CalendarEventDetails.RequestConference"/>) and the provider
/// actually produced one. Null otherwise - including when a conference was
/// requested but the provider declined or deferred it, which is a degraded
/// booking (no join link) rather than a failed one.
/// </param>
public record CalendarEventResult(string ExternalEventId, string? MeetingUrl);
