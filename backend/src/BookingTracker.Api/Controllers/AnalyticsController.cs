using BookingTracker.Application.Analytics;
using BookingTracker.Application.Analytics.Dtos;
using BookingTracker.Application.Analytics.Queries.ExportAnalyticsCsv;
using BookingTracker.Application.Analytics.Queries.ExportAnalyticsPdf;
using BookingTracker.Application.Analytics.Queries.GetActivityFeed;
using BookingTracker.Application.Analytics.Queries.GetBookingAnalytics;
using BookingTracker.Application.Analytics.Queries.GetConversionFunnel;
using BookingTracker.Application.Analytics.Queries.GetOperationalAnalytics;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingTracker.Api.Controllers;

/// <summary>
/// Read-only analytics over the organizer's own data. Every endpoint takes the
/// same filter (date range / booking page / status) and scopes to the signed-in
/// organizer inside AnalyticsScope, so an organizer can never read another's
/// numbers by passing a page id they don't own.
///
/// Split into four endpoints rather than one so the dashboard can render the
/// booking panels as soon as they arrive instead of waiting on calendar and
/// activity queries - and so a panel nobody opens costs nothing.
/// </summary>
[ApiController]
[Authorize]
[Route("api/organizer/analytics")]
public class AnalyticsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUser;

    public AnalyticsController(IMediator mediator, ICurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    private Guid OrganizerId => _currentUser.OrganizerId
        ?? throw new InvalidOperationException("Authenticated request is missing the organizer id claim.");

    /// <summary>Headline counts, trend, status split, weekday/hour distribution, completion timings, per-page performance.</summary>
    [HttpGet("bookings")]
    public async Task<ActionResult<BookingAnalyticsDto>> GetBookingAnalytics(
        [FromQuery] AnalyticsFilterRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetBookingAnalyticsQuery(BuildFilter(request)), cancellationToken);
        return Ok(result);
    }

    /// <summary>Conversion funnel and abandonment analysis, derived from the booking-session event log.</summary>
    [HttpGet("funnel")]
    public async Task<ActionResult<ConversionFunnelDto>> GetConversionFunnel(
        [FromQuery] AnalyticsFilterRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetConversionFunnelQuery(BuildFilter(request)), cancellationToken);
        return Ok(result);
    }

    /// <summary>Email, reminder, and calendar-sync health.</summary>
    [HttpGet("operations")]
    public async Task<ActionResult<OperationalAnalyticsDto>> GetOperationalAnalytics(
        [FromQuery] AnalyticsFilterRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetOperationalAnalyticsQuery(BuildFilter(request)), cancellationToken);
        return Ok(result);
    }

    /// <summary>Recent activity, merged from the existing event log, failed notifications, and page creation.</summary>
    [HttpGet("activity")]
    public async Task<ActionResult<IReadOnlyList<ActivityEntryDto>>> GetActivityFeed(
        [FromQuery] AnalyticsFilterRequest request, [FromQuery] int limit = 25, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetActivityFeedQuery(BuildFilter(request), limit), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// The whole dashboard as a CSV download - every panel, one section each,
    /// under the same filter the screen is showing.
    /// </summary>
    [HttpGet("export/csv")]
    public async Task<IActionResult> ExportCsv(
        [FromQuery] AnalyticsFilterRequest request, CancellationToken cancellationToken)
    {
        var file = await _mediator.Send(new ExportAnalyticsCsvQuery(BuildFilter(request)), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>The same data as a printable report. See ExportCsv - the two differ only in which writer renders the report.</summary>
    [HttpGet("export/pdf")]
    public async Task<IActionResult> ExportPdf(
        [FromQuery] AnalyticsFilterRequest request, CancellationToken cancellationToken)
    {
        var file = await _mediator.Send(new ExportAnalyticsPdfQuery(BuildFilter(request)), cancellationToken);
        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>
    /// Maps the query string onto the shared filter. An unrecognized status is
    /// ignored rather than rejected: a filter value is a view preference, and
    /// failing the whole dashboard over one stale query-string param would be worse
    /// than showing unfiltered numbers.
    /// </summary>
    private AnalyticsFilter BuildFilter(AnalyticsFilterRequest request)
    {
        BookingSessionStatus? status = null;
        if (!string.IsNullOrWhiteSpace(request.Status)
            && Enum.TryParse<BookingSessionStatus>(request.Status, ignoreCase: true, out var parsed))
        {
            status = parsed;
        }

        return new AnalyticsFilter(OrganizerId, request.From, request.To, request.BookingPageId, status);
    }
}

/// <summary>Query-string shape of AnalyticsFilter. OrganizerId is deliberately absent - it always comes from the token, never the caller.</summary>
public record AnalyticsFilterRequest(DateOnly? From, DateOnly? To, Guid? BookingPageId, string? Status);
