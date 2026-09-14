using System.Data;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Infrastructure.Persistence;

public class BookingTrackerDbContext : DbContext, IBookingTrackerDbContext
{
    public BookingTrackerDbContext(DbContextOptions<BookingTrackerDbContext> options) : base(options)
    {
    }

    public DbSet<Organizer> Organizers => Set<Organizer>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<BookingPage> BookingPages => Set<BookingPage>();
    public DbSet<BookingSession> BookingSessions => Set<BookingSession>();
    public DbSet<BookingSessionEvent> BookingSessionEvents => Set<BookingSessionEvent>();
    public DbSet<WorkingSchedule> WorkingSchedules => Set<WorkingSchedule>();
    public DbSet<WorkingDay> WorkingDays => Set<WorkingDay>();
    public DbSet<Domain.Entities.AvailabilityException> AvailabilityExceptions => Set<Domain.Entities.AvailabilityException>();
    public DbSet<Domain.Entities.AvailabilityOverride> AvailabilityOverrides => Set<Domain.Entities.AvailabilityOverride>();
    public DbSet<CalendarConnection> CalendarConnections => Set<CalendarConnection>();
    public DbSet<CalendarSyncedEvent> CalendarSyncedEvents => Set<CalendarSyncedEvent>();
    public DbSet<NotificationSettings> NotificationSettings => Set<NotificationSettings>();
    public DbSet<EmailNotification> EmailNotifications => Set<EmailNotification>();
    public DbSet<BookingReminder> BookingReminders => Set<BookingReminder>();

    /// <summary>
    /// Registered here rather than beside each <c>UseSqlServer</c> call because
    /// there are four of those - production DI plus three test hosts - and an
    /// interceptor that is missing from one of them is a suite measuring
    /// different locking from the one production runs.
    ///
    /// Harmless on the InMemory provider: command interception is a relational
    /// concept, so nothing there ever invokes it.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.AddInterceptors(new ClaimLockInterceptor());
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BookingTrackerDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    public async Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken = default)
    {
        var strategy = Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                await operation();
                await transaction.CommitAsync(cancellationToken);
            });
        }
        catch (Exception ex) when (IsLostRaceWithAnotherTransaction(ex))
        {
            // Serializable isolation prevents two racing claims from both
            // succeeding, but the way SQL Server enforces that is by killing one
            // of them - so a lost race arrives here as a provider exception, not
            // as a clean "somebody beat you to it". Left alone it reaches
            // ExceptionHandlingMiddleware as an unhandled error and the caller is
            // told the application broke, which is both wrong and, for a booking
            // guest, the difference between a dead end and the "choose another
            // time" recovery a 409 already offers.
            //
            // Translated, never swallowed: the original is the inner exception,
            // and EF has already logged it at Error level under
            // Microsoft.EntityFrameworkCore.Update. What it MEANS is deliberately
            // left to the caller - see ConcurrencyConflictException.
            throw new ConcurrencyConflictException(
                "The transaction was rolled back because it conflicted with another concurrent transaction.", ex);
        }
    }

    /// <summary>
    /// SQL Server error 1205 - "was deadlocked on lock resources with another
    /// process and has been chosen as the deadlock victim. Rerun the
    /// transaction."
    ///
    /// This set is deliberately one code long, and established by observation
    /// rather than from the list of things that sound related - the same rule
    /// SmtpFailureClassifier follows. 1205 is what two concurrent booking
    /// submissions actually produce here, reproduced in
    /// BookingTracker.SqlServerTests. 3960 (snapshot update conflict) cannot
    /// occur: this context never uses snapshot isolation. 1222 (lock request
    /// timeout) cannot occur either without a LOCK_TIMEOUT, which nothing sets.
    ///
    /// Anything unrecognised is left exactly as it was - a 500 - because a
    /// widened set here can only ever turn a genuine fault into something that
    /// looks handled.
    /// </summary>
    private static bool IsLostRaceWithAnotherTransaction(Exception ex)
        => ex.GetBaseException() is SqlException { Number: DeadlockVictimErrorNumber };

    private const int DeadlockVictimErrorNumber = 1205;
}
