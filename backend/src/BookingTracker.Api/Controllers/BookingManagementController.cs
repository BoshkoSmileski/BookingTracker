using BookingTracker.Api.Common;
using BookingTracker.Application.Bookings.Commands.CancelBooking;
using BookingTracker.Application.Bookings.Commands.RescheduleBooking;
using BookingTracker.Application.Bookings.Dtos;
using BookingTracker.Application.Bookings.Queries.GetBookingByToken;
using BookingTracker.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BookingTracker.Api.Controllers;

/// <summary>
/// Public - no authentication. The PublicToken in the route is the sole
/// credential; every action here is rate-limited (see Program.cs's
/// "public-token" policy) since a token is a bearer secret that must resist
/// brute-force guessing.
/// </summary>
[ApiController]
[Route("api/bookings")]
[EnableRateLimiting("public-token")]
public class BookingManagementController : ControllerBase
{
    private readonly IMediator _mediator;

    public BookingManagementController(IMediator mediator) => _mediator = mediator;

    [HttpGet("{token}")]
    public async Task<ActionResult<PublicBookingDto>> GetByToken(string token, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetBookingByTokenQuery(token), cancellationToken));

    [HttpPost("{token}/cancel")]
    public async Task<ActionResult<BookingConfirmationDto>> Cancel(string token, CancelBookingRequest request, CancellationToken cancellationToken)
    {
        var (ip, userAgent) = ClientContextAccessor.Read(HttpContext);
        var result = await _mediator.Send(
            new CancelBookingCommand(null, token, null, CancelledByType.Customer, request.Reason, ip, userAgent), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{token}/reschedule")]
    public async Task<ActionResult<BookingConfirmationDto>> Reschedule(string token, RescheduleBookingRequest request, CancellationToken cancellationToken)
    {
        var (ip, userAgent) = ClientContextAccessor.Read(HttpContext);
        var result = await _mediator.Send(
            new RescheduleBookingCommand(null, token, null, request.NewDate, request.NewTime, ip, userAgent), cancellationToken);
        return Ok(result);
    }
}

public record CancelBookingRequest(string? Reason);
public record RescheduleBookingRequest(DateOnly NewDate, TimeOnly NewTime);
