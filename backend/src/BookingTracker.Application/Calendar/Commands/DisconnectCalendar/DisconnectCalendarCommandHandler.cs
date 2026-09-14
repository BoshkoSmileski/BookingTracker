using BookingTracker.Application.Common.Interfaces;
using MediatR;

namespace BookingTracker.Application.Calendar.Commands.DisconnectCalendar;

public class DisconnectCalendarCommandHandler : IRequestHandler<DisconnectCalendarCommand>
{
    private readonly ICalendarConnectionService _connectionService;

    public DisconnectCalendarCommandHandler(ICalendarConnectionService connectionService) => _connectionService = connectionService;

    public Task Handle(DisconnectCalendarCommand request, CancellationToken cancellationToken) =>
        _connectionService.DisconnectAsync(request.OrganizerId, cancellationToken);
}
