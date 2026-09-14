using BookingTracker.Application.BookingSessions.Dtos;
using BookingTracker.Application.Common.Interfaces;

namespace BookingTracker.UnitTests.TestSupport;

/// <summary>Hand-written test double - records what was broadcast instead of standing up SignalR.</summary>
public sealed class FakeEventBroadcaster : IEventBroadcaster
{
    public List<BookingSessionEventDto> BroadcastEvents { get; } = [];
    public List<BookingSessionDto> BroadcastSessionUpdates { get; } = [];

    public Task BroadcastEventAsync(BookingSessionEventDto @event, CancellationToken cancellationToken = default)
    {
        BroadcastEvents.Add(@event);
        return Task.CompletedTask;
    }

    public Task BroadcastSessionUpdatedAsync(BookingSessionDto session, CancellationToken cancellationToken = default)
    {
        BroadcastSessionUpdates.Add(session);
        return Task.CompletedTask;
    }
}
