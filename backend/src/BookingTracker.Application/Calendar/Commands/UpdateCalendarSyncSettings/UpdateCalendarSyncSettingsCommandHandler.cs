using BookingTracker.Application.Calendar.Dtos;
using BookingTracker.Application.Common.Interfaces;
using MediatR;

namespace BookingTracker.Application.Calendar.Commands.UpdateCalendarSyncSettings;

public class UpdateCalendarSyncSettingsCommandHandler : IRequestHandler<UpdateCalendarSyncSettingsCommand, CalendarConnectionDto>
{
    private readonly ICalendarConnectionService _connectionService;

    public UpdateCalendarSyncSettingsCommandHandler(ICalendarConnectionService connectionService) => _connectionService = connectionService;

    public Task<CalendarConnectionDto> Handle(UpdateCalendarSyncSettingsCommand request, CancellationToken cancellationToken) =>
        _connectionService.UpdateSyncSettingsAsync(request.OrganizerId, request.ImportBusyEvents, request.ExportBookings, cancellationToken);
}
