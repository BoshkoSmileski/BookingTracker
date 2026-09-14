using BookingTracker.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace BookingTracker.Infrastructure.Notifications;

public class FrontendLinkBuilder : IFrontendLinkBuilder
{
    private readonly string _baseUrl;

    public FrontendLinkBuilder(IOptions<FrontendSettings> settings)
    {
        _baseUrl = settings.Value.BaseUrl.TrimEnd('/');
    }

    public string BuildManageBookingUrl(string publicToken) => $"{_baseUrl}/manage/{publicToken}";

    public string BuildCancelBookingUrl(string publicToken) => $"{_baseUrl}/manage/{publicToken}/cancel";

    public string BuildRescheduleBookingUrl(string publicToken) => $"{_baseUrl}/manage/{publicToken}/reschedule";

    // Matches App.tsx's `/book/:slug` route. A slug is already URL-safe by
    // construction (CreateBookingPageCommandHandler generates it), so it is
    // interpolated exactly as the public token above is.
    public string BuildBookingPageUrl(string slug) => $"{_baseUrl}/book/{slug}";

    public string BuildCalendarSettingsUrl(Guid pageId, string? query = null) =>
        $"{_baseUrl}/dashboard/{pageId}/settings/calendar" + (query is null ? "" : $"?{query}");
}
