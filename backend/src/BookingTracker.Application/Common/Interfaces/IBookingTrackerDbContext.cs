using BookingTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.Common.Interfaces;

/// <summary>
/// Persistence seam between Application and Infrastructure. The Application layer
/// depends only on this abstraction (and EF Core's DbSet abstraction), never on the
/// concrete DbContext or SQL Server provider - those live in Infrastructure.
/// </summary>
public interface IBookingTrackerDbContext
{
    DbSet<Organizer> Organizers { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<BookingPage> BookingPages { get; }
    DbSet<BookingSession> BookingSessions { get; }
    DbSet<BookingSessionEvent> BookingSessionEvents { get; }
    DbSet<WorkingSchedule> WorkingSchedules { get; }
    DbSet<WorkingDay> WorkingDays { get; }
    DbSet<AvailabilityException> AvailabilityExceptions { get; }

    /// <summary>Date-specific opening hours. The availability model's second producer of open time - see AvailabilityOverride.</summary>
    DbSet<AvailabilityOverride> AvailabilityOverrides { get; }
    DbSet<CalendarConnection> CalendarConnections { get; }
    DbSet<CalendarSyncedEvent> CalendarSyncedEvents { get; }
    DbSet<NotificationSettings> NotificationSettings { get; }
    DbSet<EmailNotification> EmailNotifications { get; }
    DbSet<BookingReminder> BookingReminders { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside a serializable-isolation database
    /// transaction. Used only where a "check nothing conflicts, then write" sequence
    /// must be race-condition safe against a concurrent request doing the same check
    /// (double-booking prevention) - everywhere else, a plain SaveChangesAsync call
    /// already provides enough consistency.
    /// </summary>
    Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken = default);
}
