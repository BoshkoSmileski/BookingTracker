using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Domain.Entities;

namespace BookingTracker.UnitTests.TestSupport;

/// <summary>
/// Hand-written test double that records which scheduling operations a handler
/// asked for, so handler tests can assert the wiring (e.g. "cancelling a
/// booking cancels its reminders") without also re-testing
/// BookingReminderScheduler's own behaviour - that has its own dedicated tests
/// against a real InMemory DbContext.
/// </summary>
public sealed class FakeBookingReminderScheduler : IBookingReminderScheduler
{
    public List<Guid> ScheduledFor { get; } = [];
    public List<Guid> RescheduledFor { get; } = [];
    public List<(Guid SessionId, string Reason)> CancelledFor { get; } = [];
    public List<Guid> ResyncedOrganizers { get; } = [];

    public Task ScheduleForBookingAsync(BookingSession session, CancellationToken cancellationToken = default)
    {
        ScheduledFor.Add(session.Id);
        return Task.CompletedTask;
    }

    public Task RescheduleForBookingAsync(BookingSession session, CancellationToken cancellationToken = default)
    {
        RescheduledFor.Add(session.Id);
        return Task.CompletedTask;
    }

    public Task CancelForBookingAsync(BookingSession session, string reason, CancellationToken cancellationToken = default)
    {
        CancelledFor.Add((session.Id, reason));
        return Task.CompletedTask;
    }

    public Task ResyncForOrganizerAsync(Guid organizerId, CancellationToken cancellationToken = default)
    {
        ResyncedOrganizers.Add(organizerId);
        return Task.CompletedTask;
    }
}
