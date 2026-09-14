using System.Text.Json;
using BookingTracker.Api.Common;
using BookingTracker.Application.BookingSessions.Commands.AppendBookingEvents;
using BookingTracker.Application.BookingSessions.Commands.SubmitBookingSession;
using BookingTracker.Application.BookingSessions.Dtos;
using BookingTracker.Application.BookingSessions.Queries.GetBookingSession;
using BookingTracker.Application.BookingSessions.Queries.RebuildBookingSessionState;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BookingTracker.Api.Controllers;

[ApiController]
[Route("api/booking-sessions")]
public class BookingSessionsController : ControllerBase
{
    private static readonly JsonSerializerOptions BeaconJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IMediator _mediator;

    public BookingSessionsController(IMediator mediator) => _mediator = mediator;

    /// <summary>
    /// Anonymous by necessity: this is how the wizard RESUMES an interrupted
    /// booking. useBookingSessionTracker keeps the session id in sessionStorage
    /// and re-reads the session on mount to restore the guest's own half-filled
    /// form, and the guest has no credential at that point - the PublicToken
    /// does not exist until submit.
    ///
    /// It therefore returns the guest's own name/email/phone/message/answers to
    /// the guest's own browser, which is the point. BookingSessionDto carries no
    /// PublicToken (see BookingSessionMappings.ToConfirmationDto for the one that
    /// does), no client IP, no User-Agent and no per-keystroke history - so this
    /// is the whole of the anonymous PII surface, and it is the minimum the
    /// documented resume flow needs.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BookingSessionDto>> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new GetBookingSessionQuery(id), cancellationToken));

    // There is deliberately NO anonymous timeline action here. It had
    // zero callers (frontend, e2e and backend all read the timeline through the
    // organizer route below), while returning far more than the resume flow
    // above - every intermediate keystroke as its own FieldChanged, the client
    // IP and User-Agent on every event, the booking reference, and the recipient
    // of every email sent. Authorizing it in place would have made it identical
    // to the route that already exists and is actually used:
    //
    //     GET /api/organizer/booking-pages/{pageId}/sessions/{sessionId}/timeline
    //         [Authorize] + OwnershipGuard, OrganizerController.GetSessionTimeline
    //
    // so the surface was removed rather than duplicated. See Development
    // Rule #58 and AnonymousSessionSurfaceTests.

    /// <summary>
    /// Diagnostic/proof endpoint: reconstructs the session purely from its event
    /// log and returns it, so it can be compared against GET /{id} (the projection).
    ///
    /// Anonymous, and measured to be safe rather than assumed: it returns the
    /// SAME BookingSessionDto as GET /{id} above, from the same mapper, so it
    /// discloses nothing that endpoint does not already have to. Closing this
    /// one alone would remove the thesis's event-sourcing proof without reducing
    /// what an anonymous caller can read - see AnonymousSessionSurfaceTests,
    /// which pins that equivalence so a future change cannot widen it unnoticed.
    /// </summary>
    [HttpGet("{id:guid}/rebuild")]
    public async Task<ActionResult<BookingSessionDto>> Rebuild(Guid id, CancellationToken cancellationToken)
        => Ok(await _mediator.Send(new RebuildBookingSessionStateQuery(id), cancellationToken));

    /// <summary>Main tracking endpoint - appends a batch of client-observed interactions.</summary>
    [HttpPost("{id:guid}/events")]
    [EnableRateLimiting("BookingPolicy")]
    public async Task<ActionResult<BookingSessionDto>> AppendEvents(
        Guid id,
        [FromBody] List<ClientBookingEventDto> events,
        CancellationToken cancellationToken)
    {
        var (ip, userAgent) = ClientContextAccessor.Read(HttpContext);
        var result = await _mediator.Send(new AppendBookingEventsCommand(id, events, ip, userAgent), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Same as /events, but accepts the text/plain payload navigator.sendBeacon
    /// sends on page unload - the only reliable way to flush queued events and
    /// record a BrowserClosed event when a tab closes.
    /// </summary>
    [HttpPost("{id:guid}/events/beacon")]
    [EnableRateLimiting("BookingPolicy")]
    public async Task<IActionResult> AppendEventsBeacon(Guid id, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body)) return Ok();

        var events = JsonSerializer.Deserialize<List<ClientBookingEventDto>>(body, BeaconJsonOptions) ?? [];
        if (events.Count == 0) return Ok();

        var (ip, userAgent) = ClientContextAccessor.Read(HttpContext);
        await _mediator.Send(new AppendBookingEventsCommand(id, events, ip, userAgent), cancellationToken);
        return Ok();
    }

    [HttpPost("{id:guid}/submit")]
    [EnableRateLimiting("BookingPolicy")]
    public async Task<ActionResult<BookingSessionDto>> Submit(
        Guid id,
        [FromBody] SubmitBookingSessionRequest request,
        CancellationToken cancellationToken)
    {
        var (ip, userAgent) = ClientContextAccessor.Read(HttpContext);
        var result = await _mediator.Send(
            new SubmitBookingSessionCommand(id, request.ClientSequenceNumber, ip, userAgent), cancellationToken);
        return Ok(result);
    }
}

public record SubmitBookingSessionRequest(int ClientSequenceNumber);
