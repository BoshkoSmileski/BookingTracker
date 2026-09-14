using BookingTracker.Application.Calendar.Dtos;
using BookingTracker.Application.Common.Interfaces;
using MediatR;

namespace BookingTracker.Application.Calendar.Queries.GetAvailableCalendars;

public class GetAvailableCalendarsQueryHandler : IRequestHandler<GetAvailableCalendarsQuery, IReadOnlyList<ExternalCalendarDto>>
{
    private readonly ICalendarConnectionService _connectionService;

    public GetAvailableCalendarsQueryHandler(ICalendarConnectionService connectionService) => _connectionService = connectionService;

    public Task<IReadOnlyList<ExternalCalendarDto>> Handle(GetAvailableCalendarsQuery request, CancellationToken cancellationToken) =>
        _connectionService.ListAvailableCalendarsAsync(request.OrganizerId, cancellationToken);
}
