using BookingTracker.Application.Analytics.Dtos;
using BookingTracker.Application.Analytics.Export;
using BookingTracker.Application.Analytics.Queries.GetAnalyticsReport;
using MediatR;

namespace BookingTracker.Application.Analytics.Queries.ExportAnalyticsCsv;

/// <summary>
/// Fetches the report through GetAnalyticsReportQuery and hands it to the CSV
/// writer. Deliberately thin: the ownership check lives in AnalyticsScope, the
/// numbers live in the four analytics handlers, and the formatting lives in
/// AnalyticsCsvWriter - so there is nothing left here to get wrong.
/// </summary>
public class ExportAnalyticsCsvQueryHandler : IRequestHandler<ExportAnalyticsCsvQuery, AnalyticsExportFileDto>
{
    private readonly ISender _sender;

    public ExportAnalyticsCsvQueryHandler(ISender sender) => _sender = sender;

    public async Task<AnalyticsExportFileDto> Handle(ExportAnalyticsCsvQuery request, CancellationToken cancellationToken)
    {
        var report = await _sender.Send(new GetAnalyticsReportQuery(request.Filter), cancellationToken);

        return new AnalyticsExportFileDto(
            AnalyticsExportFileName.For(report.Meta, "csv"),
            AnalyticsCsvWriter.ContentType,
            AnalyticsCsvWriter.Write(report));
    }
}
