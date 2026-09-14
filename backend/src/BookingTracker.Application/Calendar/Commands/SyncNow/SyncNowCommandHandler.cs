using BookingTracker.Application.Calendar.Dtos;
using BookingTracker.Application.Common.Interfaces;
using MediatR;

namespace BookingTracker.Application.Calendar.Commands.SyncNow;

public class SyncNowCommandHandler : IRequestHandler<SyncNowCommand, CalendarConnectionDto>
{
    private readonly ICalendarConnectionService _connectionService;

    public SyncNowCommandHandler(ICalendarConnectionService connectionService) => _connectionService = connectionService;

    public Task<CalendarConnectionDto> Handle(SyncNowCommand request, CancellationToken cancellationToken) =>
        _connectionService.SyncNowAsync(request.OrganizerId, cancellationToken);
}
