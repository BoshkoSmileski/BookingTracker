using BookingTracker.Application.Analytics.Dtos;
using MediatR;

namespace BookingTracker.Application.Analytics.Queries.ExportAnalyticsPdf;

/// <summary>The dashboard as a printable PDF report. Takes the same filter as every analytics query.</summary>
public record ExportAnalyticsPdfQuery(AnalyticsFilter Filter) : IRequest<AnalyticsExportFileDto>;
