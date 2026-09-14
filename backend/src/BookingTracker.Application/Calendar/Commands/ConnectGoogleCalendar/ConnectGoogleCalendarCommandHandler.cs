using BookingTracker.Application.Calendar.Dtos;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Domain.Enums;
using MediatR;

namespace BookingTracker.Application.Calendar.Commands.ConnectGoogleCalendar;

public class ConnectGoogleCalendarCommandHandler : IRequestHandler<ConnectGoogleCalendarCommand, CalendarConnectionDto>
{
    private readonly ICalendarConnectionService _connectionService;

    public ConnectGoogleCalendarCommandHandler(ICalendarConnectionService connectionService) => _connectionService = connectionService;

    public Task<CalendarConnectionDto> Handle(ConnectGoogleCalendarCommand request, CancellationToken cancellationToken) =>
        _connectionService.ConnectAsync(request.OrganizerId, CalendarProviderType.Google, request.AuthorizationCode, cancellationToken);
}
