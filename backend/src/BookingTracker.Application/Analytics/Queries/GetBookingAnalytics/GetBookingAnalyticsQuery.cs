using BookingTracker.Application.Analytics.Dtos;
using MediatR;

namespace BookingTracker.Application.Analytics.Queries.GetBookingAnalytics;

public record GetBookingAnalyticsQuery(AnalyticsFilter Filter) : IRequest<BookingAnalyticsDto>;
