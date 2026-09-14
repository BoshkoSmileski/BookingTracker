using BookingTracker.Application.Calendar.Dtos;
using BookingTracker.Application.Common.Interfaces;
using MediatR;

namespace BookingTracker.Application.Calendar.Queries.GetCalendarConnection;

public class GetCalendarConnectionQueryHandler : IRequestHandler<GetCalendarConnectionQuery, CalendarConnectionDto?>
{
    private readonly ICalendarConnectionService _connectionService;

    public GetCalendarConnectionQueryHandler(ICalendarConnectionService connectionService) => _connectionService = connectionService;

    public Task<CalendarConnectionDto?> Handle(GetCalendarConnectionQuery request, CancellationToken cancellationToken) =>
        _connectionService.GetConnectionAsync(request.OrganizerId, cancellationToken);
}
