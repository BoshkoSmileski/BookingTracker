using BookingTracker.Application.Analytics.Dtos;
using MediatR;

namespace BookingTracker.Application.Analytics.Queries.GetOperationalAnalytics;

public record GetOperationalAnalyticsQuery(AnalyticsFilter Filter) : IRequest<OperationalAnalyticsDto>;
