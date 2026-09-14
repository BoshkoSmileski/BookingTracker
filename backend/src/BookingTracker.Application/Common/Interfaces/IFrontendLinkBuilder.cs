namespace BookingTracker.Application.Common.Interfaces;

/// <summary>Builds visitor-facing frontend URLs for email links, keeping Application unaware of the frontend's base URL config.</summary>
public interface IFrontendLinkBuilder
{
    string BuildManageBookingUrl(string publicToken);
    string BuildCancelBookingUrl(string publicToken);
    string BuildRescheduleBookingUrl(string publicToken);

    /// <summary>
    /// The public booking page itself - the anonymous, unauthenticated screen a
    /// guest booked on in the first place. Added for the cancellation email's
    /// "Book another time" action: a cancelled booking is the one moment a guest
    /// has no manage link left worth following, and the alternative was sending
    /// them to a dashboard they cannot sign in to or building the URL a second
    /// time somewhere else.
    /// </summary>
    string BuildBookingPageUrl(string slug);

    /// <summary>Where the Google OAuth callback redirects back to. <paramref name="query"/> (if given) is appended as-is, e.g. "connected=true" or "calendarError=access_denied".</summary>
    string BuildCalendarSettingsUrl(Guid pageId, string? query = null);
}
