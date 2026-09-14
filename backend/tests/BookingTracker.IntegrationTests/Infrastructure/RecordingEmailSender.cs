using BookingTracker.Application.Calendar.Dtos;
using BookingTracker.Application.Common.Interfaces;

namespace BookingTracker.IntegrationTests.Infrastructure;

/// <summary>
/// Replaces every <see cref="IEmailSender"/> in the test host. Records in
/// memory and transmits nothing - not even to the filesystem, which the
/// development default (FileSystemEmailService) would otherwise write to under
/// %TEMP%.
///
/// In practice these tests assert on the EmailNotifications QUEUE rather than
/// on this, because EmailQueueProcessor is removed from the test host and so
/// nothing ever drains the queue to reach a sender. This exists as the
/// backstop: with it registered, a send is impossible rather than merely
/// unscheduled.
/// </summary>
public sealed class RecordingEmailSender : IEmailSender
{
    private readonly List<EmailMessage> _sent = [];

    public IReadOnlyList<EmailMessage> Sent
    {
        get { lock (_sent) return _sent.ToList(); }
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        lock (_sent) _sent.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Replaces <see cref="ICalendarSyncService"/> so no Google API call is
/// reachable from a test. Records which bookings the app tried to sync, which
/// is the only thing these tests care about - what the Google client itself
/// does is covered by the unit suite's CalendarSyncService tests.
///
/// Busy intervals default to empty, matching the fail-open contract the real
/// service has: availability is never blocked by a calendar in these tests
/// unless one explicitly sets it.
/// </summary>
public sealed class RecordingCalendarSyncService : ICalendarSyncService
{
    public IReadOnlyList<CalendarBusyIntervalDto> BusyIntervalsToReturn { get; set; } = [];

    public List<Guid> Created { get; } = [];
    public List<Guid> Rescheduled { get; } = [];
    public List<Guid> Cancelled { get; } = [];

    public Task<IReadOnlyList<CalendarBusyIntervalDto>> GetBusyIntervalsAsync(
        Guid organizerId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
        => Task.FromResult(BusyIntervalsToReturn);

    public Task SyncBookingCreatedAsync(Guid bookingSessionId, CancellationToken cancellationToken)
    {
        lock (Created) Created.Add(bookingSessionId);
        return Task.CompletedTask;
    }

    public Task SyncBookingRescheduledAsync(Guid bookingSessionId, CancellationToken cancellationToken)
    {
        lock (Rescheduled) Rescheduled.Add(bookingSessionId);
        return Task.CompletedTask;
    }

    public Task SyncBookingCancelledAsync(Guid bookingSessionId, CancellationToken cancellationToken)
    {
        lock (Cancelled) Cancelled.Add(bookingSessionId);
        return Task.CompletedTask;
    }
}
