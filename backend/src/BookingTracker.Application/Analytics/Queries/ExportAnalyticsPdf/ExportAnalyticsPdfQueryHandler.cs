using BookingTracker.Application.Analytics.Dtos;
using BookingTracker.Application.Analytics.Export;
using BookingTracker.Application.Analytics.Queries.GetAnalyticsReport;
using MediatR;

namespace BookingTracker.Application.Analytics.Queries.ExportAnalyticsPdf;

/// <summary>
/// The PDF twin of ExportAnalyticsCsvQueryHandler, and identical in shape on
/// purpose: both formats read the same report through the same query, so the two
/// downloads can never describe different numbers.
/// </summary>
public class ExportAnalyticsPdfQueryHandler : IRequestHandler<ExportAnalyticsPdfQuery, AnalyticsExportFileDto>
{
    private readonly ISender _sender;

    public ExportAnalyticsPdfQueryHandler(ISender sender) => _sender = sender;

    public async Task<AnalyticsExportFileDto> Handle(ExportAnalyticsPdfQuery request, CancellationToken cancellationToken)
    {
        var report = await _sender.Send(new GetAnalyticsReportQuery(request.Filter), cancellationToken);

        return new AnalyticsExportFileDto(
            AnalyticsExportFileName.For(report.Meta, "pdf"),
            AnalyticsPdfWriter.ContentType,
            AnalyticsPdfWriter.Write(report));
    }
}
