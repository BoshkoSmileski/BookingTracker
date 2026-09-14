using BookingTracker.Application.Availability.Dtos;
using MediatR;

namespace BookingTracker.Application.Availability.Commands.CreateAvailabilityException;

/// <param name="Date">Inclusive first day.</param>
/// <param name="EndDate">Inclusive last day. Null means a single-day exception, which is what every caller sent before ranges existed.</param>
/// <param name="StartTime">When set, blocks only this window - on every day of the range.</param>
public record CreateAvailabilityExceptionCommand(
    Guid OrganizerId,
    DateOnly Date,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    string Type,
    string? Reason,
    DateOnly? EndDate = null) : IRequest<AvailabilityExceptionDto>;
