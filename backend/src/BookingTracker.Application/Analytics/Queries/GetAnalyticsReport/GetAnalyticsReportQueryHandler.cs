using System.Globalization;
using BookingTracker.Application.Analytics.Dtos;
using BookingTracker.Application.Analytics.Queries.GetActivityFeed;
using BookingTracker.Application.Analytics.Queries.GetBookingAnalytics;
using BookingTracker.Application.Analytics.Queries.GetConversionFunnel;
using BookingTracker.Application.Analytics.Queries.GetOperationalAnalytics;
using BookingTracker.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.Analytics.Queries.GetAnalyticsReport;

/// <summary>
/// Composes the four analytics queries the dashboard already issues, plus the
/// context a document needs (who, when, which filter).
///
/// It **sends** those queries rather than re-querying: that is the whole point.
/// An export that recomputed its own aggregates would be a second implementation
/// of every metric, free to drift from the screen the moment either side
/// changed - which is exactly what resolving the filter through AnalyticsScope
/// prevents, one level up. Going through IMediator
/// also means each query still resolves its own AnalyticsScope, so ownership is
/// re-checked per query exactly as it is for the dashboard's four HTTP calls.
///
/// The cost is four scope resolutions instead of one - the same number the
/// dashboard already pays, since it makes four requests.
/// </summary>
public class GetAnalyticsReportQueryHandler : IRequestHandler<GetAnalyticsReportQuery, AnalyticsReportDto>
{
    private readonly ISender _sender;
    private readonly IBookingTrackerDbContext _db;

    public GetAnalyticsReportQueryHandler(ISender sender, IBookingTrackerDbContext db)
    {
        _sender = sender;
        _db = db;
    }

    public async Task<AnalyticsReportDto> Handle(GetAnalyticsReportQuery request, CancellationToken cancellationToken)
    {
        var filter = request.Filter;

        var bookings = await _sender.Send(new GetBookingAnalyticsQuery(filter), cancellationToken);
        var funnel = await _sender.Send(new GetConversionFunnelQuery(filter), cancellationToken);
        var operations = await _sender.Send(new GetOperationalAnalyticsQuery(filter), cancellationToken);
        var activity = await _sender.Send(new GetActivityFeedQuery(filter, request.ActivityLimit), cancellationToken);

        var meta = await BuildMetaAsync(filter, bookings.TimeZoneId, cancellationToken);

        return new AnalyticsReportDto(meta, bookings, funnel, operations, activity);
    }

    private async Task<AnalyticsReportMetaDto> BuildMetaAsync(
        AnalyticsFilter filter, string timeZoneId, CancellationToken cancellationToken)
    {
        var organizer = await _db.Organizers.AsNoTracking()
            .Where(o => o.Id == filter.OrganizerId)
            .Select(o => new { o.Name, o.Email })
            .FirstOrDefaultAsync(cancellationToken);

        // The page is known to be owned by this organizer - every query above
        // already resolved AnalyticsScope, which 404s an unowned page id.
        var pageTitle = filter.BookingPageId is { } pageId
            ? await _db.BookingPages.AsNoTracking()
                .Where(p => p.Id == pageId)
                .Select(p => p.Title)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        return new AnalyticsReportMetaDto(
            organizer?.Name ?? "Unknown organizer",
            organizer?.Email ?? string.Empty,
            DateTime.UtcNow,
            filter.From,
            filter.To,
            DescribeRange(filter.From, filter.To),
            pageTitle ?? "All booking pages",
            filter.Status?.ToString() ?? "All statuses",
            timeZoneId);
    }

    /// <summary>
    /// Both bounds are optional on the filter, so all four combinations get
    /// wording rather than an empty cell - a report that says nothing about its
    /// window cannot be interpreted later.
    /// </summary>
    private static string DescribeRange(DateOnly? from, DateOnly? to) => (from, to) switch
    {
        ({ } f, { } t) => $"{Iso(f)} to {Iso(t)}",
        ({ } f, null) => $"{Iso(f)} onwards",
        (null, { } t) => $"Up to {Iso(t)}",
        _ => "All time",
    };

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
