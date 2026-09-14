using BookingTracker.Application.Availability.Dtos;
using MediatR;

namespace BookingTracker.Application.Availability.Queries.GetWorkingSchedule;

/// <summary>Returns null (not NotFoundException) when the organizer hasn't configured a schedule yet - that's a normal state, not an error.</summary>
public record GetWorkingScheduleQuery(Guid OrganizerId) : IRequest<WorkingScheduleDto?>;
