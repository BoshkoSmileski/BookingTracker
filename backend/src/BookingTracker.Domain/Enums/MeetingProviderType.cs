namespace BookingTracker.Domain.Enums;

/// <summary>
/// How a booking's participants actually meet. Set per booking page by the
/// organizer, and snapshotted onto each BookingSession when a meeting is
/// created for it, so a booking keeps the meeting it was made with even if the
/// page is reconfigured afterwards.
///
/// Deliberately NOT paired with an IMeetingProvider interface today. A Google
/// Meet conference is created by the Calendar API as part of inserting the
/// event - there is no second service to call and no second thing to
/// abstract, so an interface with one implementation would be the speculative
/// indirection this codebase avoids everywhere else (see the same call made
/// against INotificationChannel for reminder channels).
///
/// Adding a value later:
/// - CustomLink needs only a URL column on BookingPage - the value is
///   organizer-supplied, so nothing external is called at all.
/// - Zoom/Teams need their own OAuth and their own create/update/delete API,
///   which IS the point at which extracting IMeetingProvider pays for itself.
/// </summary>
public enum MeetingProviderType
{
    /// <summary>No online meeting. The booking happens in person (or wherever the organizer's description says).</summary>
    None = 0,

    /// <summary>
    /// Google generates a Meet link as part of the calendar event this booking
    /// creates. Requires a connected Google Calendar with booking export
    /// enabled - without one there is no event to hang a conference off, and
    /// the booking simply has no meeting URL.
    /// </summary>
    GoogleMeet = 1,
}
