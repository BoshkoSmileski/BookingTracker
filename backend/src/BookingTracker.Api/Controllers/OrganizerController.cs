using BookingTracker.Application.BookingPages.Commands.BookingFormFields.AddBookingFormField;
using BookingTracker.Application.BookingPages.Commands.BookingFormFields.RemoveBookingFormField;
using BookingTracker.Application.BookingPages.Commands.BookingInstructions.AddBookingInstruction;
using BookingTracker.Application.BookingPages.Commands.BookingInstructions.RemoveBookingInstruction;
using BookingTracker.Application.BookingPages.Commands.CreateBookingPage;
using BookingTracker.Application.BookingPages.Commands.DeleteBookingPage;
using BookingTracker.Application.BookingPages.Commands.SetBookingPageActive;
using BookingTracker.Application.BookingPages.Commands.UpdateBookingPageDetails;
using BookingTracker.Application.BookingPages.Commands.UpdateBookingPageLimits;
using BookingTracker.Application.BookingPages.Commands.UpdateMeetingSettings;
using BookingTracker.Application.BookingPages.Dtos;
using BookingTracker.Application.BookingPages.Queries.GetBookingPageById;
using BookingTracker.Application.BookingPages.Queries.GetMyBookingPages;
using BookingTracker.Application.BookingSessions.Dtos;
using BookingTracker.Application.BookingSessions.Queries.GetBookingSession;
using BookingTracker.Application.BookingSessions.Queries.GetBookingSessions;
using BookingTracker.Application.BookingSessions.Queries.GetBookingSessionTimeline;
using BookingTracker.Application.Bookings.Commands.CancelBooking;
using BookingTracker.Application.Bookings.Commands.RescheduleBooking;
using BookingTracker.Application.Bookings.Commands.ResendConfirmation;
using BookingTracker.Application.Bookings.Dtos;
using BookingTracker.Application.Bookings.Queries.GetEmailHistory;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Notifications.Commands.UpdateNotificationSettings;
using BookingTracker.Application.Notifications.Dtos;
using BookingTracker.Application.Notifications.Queries.GetBookingReminders;
using BookingTracker.Application.Notifications.Queries.GetNotificationSettings;
using BookingTracker.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingTracker.Api.Controllers;

/// <summary>
/// Everything here requires an authenticated organizer, and every query that
/// takes a booking page or session id is ownership-checked in its handler
/// (see OwnershipGuard) - an organizer can never read another organizer's data
/// just by guessing a Guid.
/// </summary>
[ApiController]
[Authorize]
[Route("api/organizer")]
public class OrganizerController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUser;

    public OrganizerController(IMediator mediator, ICurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    private Guid OrganizerId => _currentUser.OrganizerId
        ?? throw new InvalidOperationException("Authenticated request is missing the organizer id claim.");

    /// <summary>The booking pages owned by the signed-in organizer.</summary>
    [HttpGet("booking-pages")]
    public async Task<ActionResult<IReadOnlyList<BookingPageSummaryDto>>> GetMyBookingPages(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetMyBookingPagesQuery(OrganizerId), cancellationToken);
        return Ok(result);
    }

    /// <summary>Creates a new booking page owned by the signed-in organizer. The slug is derived from the title.</summary>
    [HttpPost("booking-pages")]
    public async Task<ActionResult<BookingPageDetailDto>> CreateBookingPage(
        [FromBody] CreateBookingPageRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new CreateBookingPageCommand(
                OrganizerId, request.Title, request.Description, request.DurationMinutes,
                request.BufferBeforeMinutes, request.BufferAfterMinutes,
                request.MinNoticeMinutes, request.MaxBookingWindowDays, request.MaxBookingsPerDay,
                request.TimeZoneId),
            cancellationToken);
        return CreatedAtAction(nameof(GetBookingPage), new { pageId = result.Id }, result);
    }

    /// <summary>Ownership-checked full detail view of a single booking page, used to prefill the edit/settings forms.</summary>
    [HttpGet("booking-pages/{pageId:guid}")]
    public async Task<ActionResult<BookingPageDetailDto>> GetBookingPage(Guid pageId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetBookingPageByIdQuery(pageId, OrganizerId), cancellationToken);
        return Ok(result);
    }

    [HttpPut("booking-pages/{pageId:guid}/details")]
    public async Task<ActionResult<BookingPageDetailDto>> UpdateBookingPageDetails(
        Guid pageId, [FromBody] UpdateDetailsRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateBookingPageDetailsCommand(OrganizerId, pageId, request.Title, request.Description), cancellationToken);
        return Ok(result);
    }

    [HttpPut("booking-pages/{pageId:guid}/limits")]
    public async Task<ActionResult<BookingPageDetailDto>> UpdateBookingPageLimits(
        Guid pageId, [FromBody] UpdateLimitsRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateBookingPageLimitsCommand(
                OrganizerId, pageId, request.MinNoticeMinutes, request.MaxBookingWindowDays, request.MaxBookingsPerDay),
            cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Sets how bookings on this page meet. "GoogleMeet" makes each new booking's
    /// calendar event carry a Meet conference Google generates; "None" is in
    /// person. Affects future bookings only - a meeting link already issued to a
    /// guest is never revoked by a settings change.
    /// </summary>
    [HttpPut("booking-pages/{pageId:guid}/meeting")]
    public async Task<ActionResult<BookingPageDetailDto>> UpdateBookingPageMeetingSettings(
        Guid pageId, [FromBody] UpdateMeetingRequest request, CancellationToken cancellationToken)
    {
        // Parsed here rather than bound straight to the enum, for the same
        // reason AddFormFieldRequest is: this API carries enums as strings in
        // both directions and registers no JsonStringEnumConverter, so binding
        // the enum would demand a numeric value from clients and answer a typo
        // with a framework-shaped 400 instead of this API's own.
        if (!Enum.TryParse<MeetingProviderType>(request.MeetingProvider, ignoreCase: true, out var provider)
            || !Enum.IsDefined(provider))
        {
            return BadRequest($"Unknown meeting provider '{request.MeetingProvider}'. Expected None or GoogleMeet.");
        }

        var result = await _mediator.Send(new UpdateMeetingSettingsCommand(OrganizerId, pageId, provider), cancellationToken);
        return Ok(result);
    }

    /// <summary>Enables or disables the page - a disabled page's public booking link and slot listing both stop working (existing GetBookingPageBySlugQuery already filters on IsActive).</summary>
    [HttpPatch("booking-pages/{pageId:guid}/active")]
    public async Task<ActionResult<BookingPageDetailDto>> SetBookingPageActive(
        Guid pageId, [FromBody] SetActiveRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SetBookingPageActiveCommand(OrganizerId, pageId, request.IsActive), cancellationToken);
        return Ok(result);
    }

    /// <summary>Blocked with 409 if the page has any confirmed (Submitted) bookings - disable it instead.</summary>
    [HttpDelete("booking-pages/{pageId:guid}")]
    public async Task<IActionResult> DeleteBookingPage(Guid pageId, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteBookingPageCommand(OrganizerId, pageId), cancellationToken);
        return NoContent();
    }

    /// <summary>Guidance shown to visitors before they book - read, never answered.</summary>
    [HttpPost("booking-pages/{pageId:guid}/instructions")]
    public async Task<ActionResult<BookingPageDetailDto>> AddBookingInstruction(
        Guid pageId, [FromBody] AddInstructionRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new AddBookingInstructionCommand(OrganizerId, pageId, request.Text), cancellationToken);
        return Ok(result);
    }

    [HttpDelete("booking-pages/{pageId:guid}/instructions/{instructionId:guid}")]
    public async Task<ActionResult<BookingPageDetailDto>> RemoveBookingInstruction(
        Guid pageId, Guid instructionId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RemoveBookingInstructionCommand(OrganizerId, pageId, instructionId), cancellationToken);
        return Ok(result);
    }

    /// <summary>Questions visitors answer while booking - the answerable counterpart to instructions above.</summary>
    [HttpPost("booking-pages/{pageId:guid}/form-fields")]
    public async Task<ActionResult<BookingPageDetailDto>> AddBookingFormField(
        Guid pageId, [FromBody] AddFormFieldRequest request, CancellationToken cancellationToken)
    {
        // Parsed here rather than bound straight to the enum: this API carries
        // enums as strings in both directions (mappings call .ToString()), and
        // no JsonStringEnumConverter is registered - so binding the enum
        // directly would demand a numeric "type": 0 from clients and answer a
        // typo with a framework-shaped 400 instead of this API's own. Same
        // shape as GetSessions' status below.
        if (!Enum.TryParse<BookingFieldType>(request.Type, ignoreCase: true, out var type))
            return BadRequest($"Unknown field type '{request.Type}'. Expected ShortText or LongText.");

        var result = await _mediator.Send(
            new AddBookingFormFieldCommand(OrganizerId, pageId, request.Label, type, request.IsRequired),
            cancellationToken);
        return Ok(result);
    }

    [HttpDelete("booking-pages/{pageId:guid}/form-fields/{fieldId:guid}")]
    public async Task<ActionResult<BookingPageDetailDto>> RemoveBookingFormField(
        Guid pageId, Guid fieldId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new RemoveBookingFormFieldCommand(OrganizerId, pageId, fieldId), cancellationToken);
        return Ok(result);
    }

    /// <summary>Backs the dashboard's Active / Submitted / Abandoned session lists.</summary>
    [HttpGet("booking-pages/{pageId:guid}/sessions")]
    public async Task<ActionResult<IReadOnlyList<BookingSessionDto>>> GetSessions(
        Guid pageId,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        BookingSessionStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<BookingSessionStatus>(status, ignoreCase: true, out var value))
                return BadRequest($"Unknown status '{status}'. Expected Active, Submitted, Abandoned, or Cancelled.");
            parsedStatus = value;
        }

        var result = await _mediator.Send(new GetBookingSessionsQuery(pageId, OrganizerId, parsedStatus), cancellationToken);
        return Ok(result);
    }

    /// <summary>Ownership-checked session detail, for the dashboard's session view.</summary>
    [HttpGet("booking-pages/{pageId:guid}/sessions/{sessionId:guid}")]
    public async Task<ActionResult<BookingSessionDto>> GetSession(Guid pageId, Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetBookingSessionQuery(sessionId, OrganizerId), cancellationToken);
        return Ok(result);
    }

    /// <summary>Ownership-checked timeline, for the dashboard's session view.</summary>
    [HttpGet("booking-pages/{pageId:guid}/sessions/{sessionId:guid}/timeline")]
    public async Task<ActionResult<IReadOnlyList<BookingSessionEventDto>>> GetSessionTimeline(
        Guid pageId, Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetBookingSessionTimelineQuery(sessionId, OrganizerId), cancellationToken);
        return Ok(result);
    }

    /// <summary>Organizer-initiated cancellation - ownership is enforced inside the handler via RequestingOrganizerId.</summary>
    [HttpPost("booking-pages/{pageId:guid}/sessions/{sessionId:guid}/cancel")]
    public async Task<ActionResult<BookingConfirmationDto>> CancelSession(
        Guid pageId, Guid sessionId, [FromBody] OrganizerCancelRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new CancelBookingCommand(sessionId, null, OrganizerId, CancelledByType.Organizer, request.Reason, null, null), cancellationToken);
        return Ok(result);
    }

    /// <summary>Organizer-initiated reschedule - ownership is enforced inside the handler via RequestingOrganizerId.</summary>
    [HttpPost("booking-pages/{pageId:guid}/sessions/{sessionId:guid}/reschedule")]
    public async Task<ActionResult<BookingConfirmationDto>> RescheduleSession(
        Guid pageId, Guid sessionId, [FromBody] OrganizerRescheduleRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new RescheduleBookingCommand(sessionId, null, OrganizerId, request.NewDate, request.NewTime, null, null), cancellationToken);
        return Ok(result);
    }

    [HttpPost("booking-pages/{pageId:guid}/sessions/{sessionId:guid}/resend-confirmation")]
    public async Task<IActionResult> ResendConfirmation(Guid pageId, Guid sessionId, CancellationToken cancellationToken)
    {
        await _mediator.Send(new ResendConfirmationCommand(sessionId, OrganizerId), cancellationToken);
        return NoContent();
    }

    [HttpGet("booking-pages/{pageId:guid}/sessions/{sessionId:guid}/email-history")]
    public async Task<ActionResult<IReadOnlyList<BookingSessionEventDto>>> GetEmailHistory(
        Guid pageId, Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetEmailHistoryQuery(sessionId, OrganizerId), cancellationToken);
        return Ok(result);
    }

    /// <summary>Scheduling + delivery status of every reminder for one booking (upcoming, queued, sent, failed, cancelled, skipped).</summary>
    [HttpGet("booking-pages/{pageId:guid}/sessions/{sessionId:guid}/reminders")]
    public async Task<ActionResult<IReadOnlyList<BookingReminderDto>>> GetSessionReminders(
        Guid pageId, Guid sessionId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetBookingRemindersQuery(sessionId, OrganizerId), cancellationToken);
        return Ok(result);
    }

    /// <summary>Which automatic notification emails go out for this organizer's bookings - defaults apply until the organizer saves their own.</summary>
    [HttpGet("notification-settings")]
    public async Task<ActionResult<NotificationSettingsDto>> GetNotificationSettings(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetNotificationSettingsQuery(OrganizerId), cancellationToken);
        return Ok(result);
    }

    [HttpPut("notification-settings")]
    public async Task<ActionResult<NotificationSettingsDto>> UpdateNotificationSettings(
        [FromBody] UpdateNotificationSettingsRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateNotificationSettingsCommand(
                OrganizerId, request.NotifyGuestOnBooking, request.NotifyOrganizerOnBooking,
                request.RemindersEnabled, request.ReminderMinutesBeforeEvent, request.NotifyOrganizerOnReminderSent),
            cancellationToken);
        return Ok(result);
    }
}

public record OrganizerCancelRequest(string? Reason);
public record OrganizerRescheduleRequest(DateOnly NewDate, TimeOnly NewTime);

/// <param name="TimeZoneId">
/// Optional. The organizer's IANA zone as their browser resolved it, used only
/// to decide which clock a first working schedule is created on - see
/// CreateBookingPageCommand. Omitting it is fine; an organizer who already has
/// a schedule ignores it either way.
/// </param>
public record CreateBookingPageRequest(
    string Title, string? Description, int DurationMinutes, int BufferBeforeMinutes, int BufferAfterMinutes,
    int? MinNoticeMinutes, int? MaxBookingWindowDays, int? MaxBookingsPerDay, string? TimeZoneId = null);
public record UpdateDetailsRequest(string Title, string? Description);
public record UpdateLimitsRequest(int? MinNoticeMinutes, int? MaxBookingWindowDays, int? MaxBookingsPerDay);
public record SetActiveRequest(bool IsActive);

/// <param name="MeetingProvider">"None" or "GoogleMeet" - a string, matching how every other enum crosses this API.</param>
public record UpdateMeetingRequest(string MeetingProvider);
public record AddInstructionRequest(string Text);

/// <param name="Type">"ShortText" or "LongText" - a string, matching how every other enum crosses this API.</param>
public record AddFormFieldRequest(string Label, string Type, bool IsRequired);
public record UpdateNotificationSettingsRequest(
    bool NotifyGuestOnBooking, bool NotifyOrganizerOnBooking, bool RemindersEnabled, IReadOnlyList<int> ReminderMinutesBeforeEvent,
    bool NotifyOrganizerOnReminderSent);
