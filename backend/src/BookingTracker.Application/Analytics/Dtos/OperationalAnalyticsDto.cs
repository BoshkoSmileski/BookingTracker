namespace BookingTracker.Application.Analytics.Dtos;

/// <summary>
/// Delivery-side health: outbound email, reminders, and calendar sync. Grouped
/// into one query because they answer the same operator question ("is anything
/// silently broken?") and are read from three small aggregate queries rather
/// than the booking tables the rest of the dashboard uses.
/// </summary>
public record OperationalAnalyticsDto(
    EmailAnalyticsDto Email,
    ReminderAnalyticsDto Reminders,
    CalendarAnalyticsDto Calendar);

/// <param name="Retries">Total failed delivery attempts across all notifications - EmailNotification.AttemptCount only counts failures.</param>
/// <param name="ByType">Sent counts per notification type (confirmation, cancellation, reschedule, reminder, organizer notices).</param>
public record EmailAnalyticsDto(
    int Total,
    int Pending,
    int Sent,
    int Failed,
    int Retries,
    double DeliveryRate,
    IReadOnlyList<EmailTypeCountDto> ByType);

public record EmailTypeCountDto(string NotificationType, int Total, int Sent, int Failed);

/// <param name="AverageLeadTimeMinutes">Mean configured lead time across reminders in scope - what organizers actually chose in practice.</param>
public record ReminderAnalyticsDto(
    int Total,
    int Scheduled,
    int Sent,
    int Failed,
    int Skipped,
    int Cancelled,
    double? AverageLeadTimeMinutes);

/// <param name="SyncCoverage">
/// Synced bookings / confirmed bookings, 0-1. Deliberately NOT called a "success
/// rate": nothing records per-attempt sync outcomes, only the last success and
/// last failure timestamps, so a true success rate is not derivable from the
/// data. Coverage is - and it answers the question an organizer actually has.
/// </param>
public record CalendarAnalyticsDto(
    bool Connected,
    string? AccountEmail,
    string? CalendarName,
    string? Status,
    string? HealthStatus,
    int SyncedBookings,
    int ConfirmedBookings,
    double? SyncCoverage,
    DateTime? LastSuccessfulSyncAtUtc,
    DateTime? LastFailedSyncAtUtc,
    string? LastSyncError);
