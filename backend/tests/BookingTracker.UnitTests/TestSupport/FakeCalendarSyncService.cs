using BookingTracker.Application.Calendar.Dtos;
using BookingTracker.Application.Common.Interfaces;

namespace BookingTracker.UnitTests.TestSupport;

/// <summary>
/// Hand-written test double instead of a mocking library: the interface is
/// small and GetAvailableSlotsQueryHandlerTests only ever needs to control
/// what GetBusyIntervalsAsync returns and record how it was called.
/// </summary>
public sealed class FakeCalendarSyncService : ICalendarSyncService
{
    public IReadOnlyList<CalendarBusyIntervalDto> BusyIntervalsToReturn { get; set; } = [];
    public (Guid OrganizerId, DateTime FromUtc, DateTime ToUtc)? LastGetBusyIntervalsCall { get; private set; }

    public Task<IReadOnlyList<CalendarBusyIntervalDto>> GetBusyIntervalsAsync(
        Guid organizerId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        LastGetBusyIntervalsCall = (organizerId, fromUtc, toUtc);
        return Task.FromResult(BusyIntervalsToReturn);
    }

    public Task SyncBookingCreatedAsync(Guid bookingSessionId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SyncBookingRescheduledAsync(Guid bookingSessionId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SyncBookingCancelledAsync(Guid bookingSessionId, CancellationToken cancellationToken) => Task.CompletedTask;
}
