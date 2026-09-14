using BookingTracker.Api.Common;
using BookingTracker.Application.Availability.Dtos;
using BookingTracker.Application.Availability.Queries.GetAvailableSlots;
using BookingTracker.Application.BookingPages.Dtos;
using BookingTracker.Application.BookingPages.Queries.GetBookingPageBySlug;
using BookingTracker.Application.BookingSessions.Commands.StartBookingSession;
using BookingTracker.Application.BookingSessions.Dtos;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BookingTracker.Api.Controllers;

[ApiController]
[Route("api/booking-pages")]
public class BookingPagesController : ControllerBase
{
    private readonly IMediator _mediator;

    public BookingPagesController(IMediator mediator) => _mediator = mediator;

    [HttpGet("{slug}")]
    public async Task<ActionResult<BookingPageDto>> GetBySlug(string slug, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetBookingPageBySlugQuery(slug), cancellationToken);
        return Ok(result);
    }

    /// <summary>Public - the only availability information a visitor can query.</summary>
    [HttpGet("{slug}/slots")]
    public async Task<ActionResult<IReadOnlyList<AvailableSlotDto>>> GetAvailableSlots(
        string slug, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken cancellationToken)
    {
        var page = await _mediator.Send(new GetBookingPageBySlugQuery(slug), cancellationToken);
        var result = await _mediator.Send(new GetAvailableSlotsQuery(page.Id, from, to), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Visitor opens the booking page - creates the session and its SessionStarted
    /// event. Anonymous and unbounded otherwise (one call per page load), so it
    /// shares the public booking wizard's "BookingPolicy" rate limit.
    /// </summary>
    [HttpPost("{slug}/sessions")]
    [EnableRateLimiting("BookingPolicy")]
    public async Task<ActionResult<BookingSessionDto>> StartSession(string slug, CancellationToken cancellationToken)
    {
        var (ip, userAgent) = ClientContextAccessor.Read(HttpContext);
        var result = await _mediator.Send(new StartBookingSessionCommand(slug, ip, userAgent), cancellationToken);
        return Created($"/api/booking-sessions/{result.Id}", result);
    }
}
