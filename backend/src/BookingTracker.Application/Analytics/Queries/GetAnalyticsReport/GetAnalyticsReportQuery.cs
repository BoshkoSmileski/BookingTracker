using BookingTracker.Application.Analytics.Dtos;
using MediatR;

namespace BookingTracker.Application.Analytics.Queries.GetAnalyticsReport;

/// <summary>
/// The whole dashboard as one result, used by both export formats.
///
/// <paramref name="ActivityLimit"/> defaults higher than the dashboard's 20
/// because a document is not screen-constrained; it is still the same feed, in
/// the same order, and is clamped by GetActivityFeedQueryHandler exactly as the
/// dashboard's request is.
/// </summary>
public record GetAnalyticsReportQuery(AnalyticsFilter Filter, int ActivityLimit = 100)
    : IRequest<AnalyticsReportDto>;
