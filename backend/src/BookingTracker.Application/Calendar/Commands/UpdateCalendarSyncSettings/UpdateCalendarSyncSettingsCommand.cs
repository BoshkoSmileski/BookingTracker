using BookingTracker.Application.Calendar.Dtos;
using MediatR;

namespace BookingTracker.Application.Calendar.Commands.UpdateCalendarSyncSettings;

public record UpdateCalendarSyncSettingsCommand(Guid OrganizerId, bool ImportBusyEvents, bool ExportBookings) : IRequest<CalendarConnectionDto>;
