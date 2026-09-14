using BookingTracker.Application.Calendar.Dtos;

namespace BookingTracker.Application.Common.Interfaces;

/// <summary>
/// The surface booking-lifecycle command handlers and the availability query
/// call into. Every method here is meant to be resilient on the caller's
/// behalf where the caller can't afford to fail (see GetBusyIntervalsAsync's
/// "fail open" contract below) - callers that CAN tolerate a thrown exception
/// (the best-effort try/catch blocks in Submit/Cancel/Reschedule handlers)
/// still get one from the Sync* methods, matching this codebase's existing
/// email-sending resilience pattern.
/// </summary>
public interface ICalendarSyncService
{
    /// <summary>
    /// Never throws - a Google outage must not break the public booking page.
    /// Returns an empty list (meaning "don't filter anything out") if the
    /// organizer has no connection, ImportBusyEvents is off, or the provider
    /// call fails for any reason; failures are logged and reflected on the
    /// CalendarConnection's Status internally.
    /// </summary>
    Task<IReadOnlyList<CalendarBusyIntervalDto>> GetBusyIntervalsAsync(
        Guid organizerId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);

    /// <summary>No-ops if the organizer has no connection or ExportBookings is off. Throws on a real provider failure - the caller decides how to handle it (matches the existing email-sending contract).</summary>
    Task SyncBookingCreatedAsync(Guid bookingSessionId, CancellationToken cancellationToken);

    Task SyncBookingRescheduledAsync(Guid bookingSessionId, CancellationToken cancellationToken);

    Task SyncBookingCancelledAsync(Guid bookingSessionId, CancellationToken cancellationToken);
}
