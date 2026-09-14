using BookingTracker.Application.Calendar.Dtos;
using MediatR;

namespace BookingTracker.Application.Calendar.Commands.ConnectGoogleCalendar;

public record ConnectGoogleCalendarCommand(Guid OrganizerId, string AuthorizationCode) : IRequest<CalendarConnectionDto>;
