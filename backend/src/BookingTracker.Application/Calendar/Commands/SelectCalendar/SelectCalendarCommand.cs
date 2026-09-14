using BookingTracker.Application.Calendar.Dtos;
using MediatR;

namespace BookingTracker.Application.Calendar.Commands.SelectCalendar;

public record SelectCalendarCommand(Guid OrganizerId, string ExternalCalendarId, string ExternalCalendarName) : IRequest<CalendarConnectionDto>;
