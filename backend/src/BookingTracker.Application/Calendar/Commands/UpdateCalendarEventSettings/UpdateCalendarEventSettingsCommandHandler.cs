using BookingTracker.Application.Calendar.Dtos;
using BookingTracker.Application.Common.Interfaces;
using MediatR;

namespace BookingTracker.Application.Calendar.Commands.UpdateCalendarEventSettings;

public class UpdateCalendarEventSettingsCommandHandler : IRequestHandler<UpdateCalendarEventSettingsCommand, CalendarConnectionDto>
{
    private readonly ICalendarConnectionService _connectionService;

    public UpdateCalendarEventSettingsCommandHandler(ICalendarConnectionService connectionService) => _connectionService = connectionService;

    public Task<CalendarConnectionDto> Handle(UpdateCalendarEventSettingsCommand request, CancellationToken cancellationToken) =>
        _connectionService.UpdateEventSettingsAsync(
            request.OrganizerId, request.EventTitleFormat, request.AutoDeleteCancelledBookings,
            request.AutoUpdateRescheduledBookings, request.DefaultReminderMinutes, request.EventVisibility, cancellationToken);
}
