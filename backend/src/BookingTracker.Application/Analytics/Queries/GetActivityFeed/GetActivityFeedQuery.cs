using BookingTracker.Application.Analytics.Dtos;
using MediatR;

namespace BookingTracker.Application.Analytics.Queries.GetActivityFeed;

public record GetActivityFeedQuery(AnalyticsFilter Filter, int Limit = 25) : IRequest<IReadOnlyList<ActivityEntryDto>>;
