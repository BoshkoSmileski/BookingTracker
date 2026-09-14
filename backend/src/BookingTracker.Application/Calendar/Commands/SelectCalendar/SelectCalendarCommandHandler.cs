using BookingTracker.Application.Calendar.Dtos;
using BookingTracker.Application.Common.Interfaces;
using MediatR;

namespace BookingTracker.Application.Calendar.Commands.SelectCalendar;

public class SelectCalendarCommandHandler : IRequestHandler<SelectCalendarCommand, CalendarConnectionDto>
{
    private readonly ICalendarConnectionService _connectionService;

    public SelectCalendarCommandHandler(ICalendarConnectionService connectionService) => _connectionService = connectionService;

    public Task<CalendarConnectionDto> Handle(SelectCalendarCommand request, CancellationToken cancellationToken) =>
        _connectionService.SelectCalendarAsync(request.OrganizerId, request.ExternalCalendarId, request.ExternalCalendarName, cancellationToken);
}
