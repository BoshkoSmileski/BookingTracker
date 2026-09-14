using BookingTracker.Application.BookingSessions.Dtos;
using BookingTracker.Application.Common.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace BookingTracker.Infrastructure.RealTime;

public class SignalREventBroadcaster : IEventBroadcaster
{
    private readonly IHubContext<OrganizerDashboardHub> _hub;

    public SignalREventBroadcaster(IHubContext<OrganizerDashboardHub> hub)
    {
        _hub = hub;
    }

    public Task BroadcastEventAsync(BookingSessionEventDto @event, CancellationToken cancellationToken = default)
        => _hub.Clients
            .Group(OrganizerDashboardHub.GroupName(@event.BookingPageId))
            .SendAsync("SessionEventAdded", @event, cancellationToken);

    public Task BroadcastSessionUpdatedAsync(BookingSessionDto session, CancellationToken cancellationToken = default)
        => _hub.Clients
            .Group(OrganizerDashboardHub.GroupName(session.BookingPageId))
            .SendAsync("SessionUpdated", session, cancellationToken);
}
