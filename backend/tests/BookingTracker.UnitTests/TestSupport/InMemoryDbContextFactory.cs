using BookingTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory.Infrastructure.Internal;

namespace BookingTracker.UnitTests.TestSupport;

/// <summary>
/// Creates a fresh <see cref="BookingTrackerDbContext"/> backed by EF Core's
/// InMemory provider - never the real SQL Server. Every call gets its own
/// uniquely-named database, so tests never see another test's data and can
/// run in parallel without interfering with each other.
/// </summary>
public static class InMemoryDbContextFactory
{
    public static BookingTrackerDbContext Create()
    {
        var options = new DbContextOptionsBuilder<BookingTrackerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableSensitiveDataLogging()
            .Options;

        return new BookingTrackerDbContext(options);
    }

    /// <summary>
    /// Same, but with the InMemory provider's "transactions are ignored"
    /// warning downgraded from an exception, so a handler that wraps its work
    /// in <c>ExecuteInTransactionAsync</c> can be driven end to end here.
    ///
    /// Use this ONLY for handlers where the transaction is incidental to what
    /// is being asserted. It genuinely does not provide isolation, so nothing
    /// about race-condition safety may be claimed from a test using it - the
    /// Serializable behaviour that makes double-booking impossible is verified
    /// against the real provider, and BookingConflictCheckerTests exercises the
    /// check itself at the service level for exactly that reason.
    /// </summary>
    public static BookingTrackerDbContext CreateIgnoringTransactions()
    {
        var options = new DbContextOptionsBuilder<BookingTrackerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableSensitiveDataLogging()
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new BookingTrackerDbContext(options);
    }
}
