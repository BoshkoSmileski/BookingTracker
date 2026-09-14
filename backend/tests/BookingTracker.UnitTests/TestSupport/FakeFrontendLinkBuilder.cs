using BookingTracker.Application.Common.Interfaces;

namespace BookingTracker.UnitTests.TestSupport;

/// <summary>Hand-written test double - deterministic, inspectable URLs instead of reading a real Frontend:BaseUrl config value.</summary>
public sealed class FakeFrontendLinkBuilder : IFrontendLinkBuilder
{
    public string BuildManageBookingUrl(string publicToken) => $"https://test.local/manage/{publicToken}";

    public string BuildCancelBookingUrl(string publicToken) => $"https://test.local/manage/{publicToken}/cancel";

    public string BuildRescheduleBookingUrl(string publicToken) => $"https://test.local/manage/{publicToken}/reschedule";

    public string BuildBookingPageUrl(string slug) => $"https://test.local/book/{slug}";

    public string BuildCalendarSettingsUrl(Guid pageId, string? query = null) =>
        $"https://test.local/dashboard/{pageId}/settings/calendar" + (query is null ? "" : $"?{query}");
}
