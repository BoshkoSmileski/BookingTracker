using BookingTracker.Application.Analytics.Dtos;
using MediatR;

namespace BookingTracker.Application.Analytics.Queries.ExportAnalyticsCsv;

/// <summary>The dashboard's data as a CSV download. Takes the same filter as every analytics query.</summary>
public record ExportAnalyticsCsvQuery(AnalyticsFilter Filter) : IRequest<AnalyticsExportFileDto>;
