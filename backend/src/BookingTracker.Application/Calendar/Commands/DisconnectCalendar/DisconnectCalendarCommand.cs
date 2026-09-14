using MediatR;

namespace BookingTracker.Application.Calendar.Commands.DisconnectCalendar;

public record DisconnectCalendarCommand(Guid OrganizerId) : IRequest;
