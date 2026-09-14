using BookingTracker.Application.BookingSessions.Dtos;

namespace BookingTracker.Application.Common.Interfaces;

/// <summary>
/// Pushes live updates to organizer dashboards. Abstracts away SignalR so the
/// Application layer never references a transport technology directly.
/// </summary>
public interface IEventBroadcaster
{
    Task BroadcastEventAsync(BookingSessionEventDto @event, CancellationToken cancellationToken = default);

    Task BroadcastSessionUpdatedAsync(BookingSessionDto session, CancellationToken cancellationToken = default);
}
