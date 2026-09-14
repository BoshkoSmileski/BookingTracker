using BookingTracker.Application.Calendar.Dtos;
using MediatR;

namespace BookingTracker.Application.Calendar.Queries.GetAvailableCalendars;

public record GetAvailableCalendarsQuery(Guid OrganizerId) : IRequest<IReadOnlyList<ExternalCalendarDto>>;
