using BookingTracker.Domain.Enums;

namespace BookingTracker.Application.Analytics;

/// <summary>
/// The filter every analytics query accepts, so "date range + booking page +
/// status" means the same thing on every card and chart. Carried as one record
/// rather than repeated parameters because the API surfaces it as one set of
/// query-string values and the dashboard applies it to all panels at once.
///
/// <paramref name="From"/>/<paramref name="To"/> are inclusive and match against
/// a session's <c>CreatedAt</c> (when the visitor arrived), not the meeting date -
/// analytics answer "what happened in this period", and a booking made in March
/// for a meeting in June belongs to March's numbers.
/// </summary>
public record AnalyticsFilter(
    Guid OrganizerId,
    DateOnly? From = null,
    DateOnly? To = null,
    Guid? BookingPageId = null,
    BookingSessionStatus? Status = null);
