using BookingTracker.Application.Analytics.Dtos;
using MediatR;

namespace BookingTracker.Application.Analytics.Queries.GetConversionFunnel;

public record GetConversionFunnelQuery(AnalyticsFilter Filter) : IRequest<ConversionFunnelDto>;
