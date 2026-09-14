using BookingTracker.Application.Availability.Commands.CreateAvailabilityException;
using BookingTracker.Application.Availability.Commands.DeleteAvailabilityException;
using BookingTracker.Application.Availability.Commands.DeleteAvailabilityOverride;
using BookingTracker.Application.Availability.Commands.SaveAvailabilityOverride;
using BookingTracker.Application.Availability.Commands.SaveWorkingSchedule;
using BookingTracker.Application.Availability.Commands.UpdateBookingPageSchedulingSettings;
using BookingTracker.Application.Availability.Dtos;
using BookingTracker.Application.Availability.Queries.GetAvailabilityExceptions;
using BookingTracker.Application.Availability.Queries.GetAvailabilityOverrides;
using BookingTracker.Application.Availability.Queries.GetWorkingSchedule;
using BookingTracker.Application.BookingPages.Dtos;
using BookingTracker.Application.Common.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingTracker.Api.Controllers;

/// <summary>
/// Everything here changes an organizer's own scheduling settings, so every
/// action requires authentication and always operates on the signed-in
/// organizer's own id - never a client-supplied one.
/// </summary>
[ApiController]
[Authorize]
[Route("api/organizer/availability")]
public class AvailabilityController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUser;

    public AvailabilityController(IMediator mediator, ICurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    private Guid OrganizerId => _currentUser.OrganizerId
        ?? throw new InvalidOperationException("Authenticated request is missing the organizer id claim.");

    [HttpGet("schedule")]
    public async Task<ActionResult<WorkingScheduleDto?>> GetSchedule(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetWorkingScheduleQuery(OrganizerId), cancellationToken);
        return Ok(result);
    }

    /// <summary>Replaces the organizer's entire weekly schedule (idempotent upsert).</summary>
    [HttpPut("schedule")]
    public async Task<ActionResult<WorkingScheduleDto>> SaveSchedule(SaveScheduleRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SaveWorkingScheduleCommand(OrganizerId, request.TimeZoneId, request.Days), cancellationToken);
        return Ok(result);
    }

    [HttpGet("exceptions")]
    public async Task<ActionResult<IReadOnlyList<AvailabilityExceptionDto>>> GetExceptions(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAvailabilityExceptionsQuery(OrganizerId, from, to), cancellationToken);
        return Ok(result);
    }

    [HttpPost("exceptions")]
    public async Task<ActionResult<AvailabilityExceptionDto>> CreateException(
        CreateExceptionRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new CreateAvailabilityExceptionCommand(
                OrganizerId, request.Date, request.StartTime, request.EndTime, request.Type, request.Reason, request.EndDate),
            cancellationToken);
        return Ok(result);
    }

    [HttpDelete("exceptions/{exceptionId:guid}")]
    public async Task<IActionResult> DeleteException(Guid exceptionId, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteAvailabilityExceptionCommand(OrganizerId, exceptionId), cancellationToken);
        return NoContent();
    }

    /// <summary>Date-specific opening hours, which replace the weekly schedule on their date.</summary>
    [HttpGet("overrides")]
    public async Task<ActionResult<IReadOnlyList<AvailabilityOverrideDto>>> GetOverrides(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAvailabilityOverridesQuery(OrganizerId, from, to), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Sets one date's hours, creating or replacing that date's override. An
    /// upsert rather than a POST/PUT pair because at most one override exists
    /// per date, so adding and editing are the same request - see
    /// SaveAvailabilityOverrideCommand. Sending an empty `ranges` array closes
    /// the day; to fall back to the weekly schedule, DELETE the override.
    /// </summary>
    [HttpPut("overrides")]
    public async Task<ActionResult<AvailabilityOverrideDto>> SaveOverride(
        SaveOverrideRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new SaveAvailabilityOverrideCommand(OrganizerId, request.Date, request.Ranges ?? [], request.Note),
            cancellationToken);
        return Ok(result);
    }

    [HttpDelete("overrides/{overrideId:guid}")]
    public async Task<IActionResult> DeleteOverride(Guid overrideId, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteAvailabilityOverrideCommand(OrganizerId, overrideId), cancellationToken);
        return NoContent();
    }

    /// <summary>Duration + before/after buffers for one of the organizer's booking pages.</summary>
    [HttpPut("booking-pages/{pageId:guid}/scheduling-settings")]
    public async Task<ActionResult<BookingPageDto>> UpdateSchedulingSettings(
        Guid pageId, UpdateSchedulingSettingsRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateBookingPageSchedulingSettingsCommand(OrganizerId, pageId, request.DurationMinutes, request.BufferBeforeMinutes, request.BufferAfterMinutes),
            cancellationToken);
        return Ok(result);
    }
}

public record SaveScheduleRequest(string TimeZoneId, IReadOnlyList<WorkingDayInput> Days);
/// <summary>EndDate is optional: omitting it blocks a single day, which is what the endpoint did before ranges existed.</summary>
public record CreateExceptionRequest(DateOnly Date, TimeOnly? StartTime, TimeOnly? EndTime, string Type, string? Reason, DateOnly? EndDate = null);
public record UpdateSchedulingSettingsRequest(int DurationMinutes, int BufferBeforeMinutes, int BufferAfterMinutes);

/// <param name="Ranges">The day's open hours. An empty array (or null) closes the date.</param>
public record SaveOverrideRequest(DateOnly Date, IReadOnlyList<TimeRangeDto>? Ranges, string? Note);
