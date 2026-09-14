using BookingTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.SqlServerTests.Infrastructure;

/// <summary>
/// Creates the throwaway database once for the whole assembly, applies the real
/// migrations to it, proves at runtime that it is the database we think it is,
/// and drops it again at the end.
///
/// **One database for the assembly, not one per class.** Applying sixteen
/// migrations costs a few seconds, so a database per class would multiply the
/// cost of a suite that is already the slowest backend one. Isolation is by
/// construction instead - every test seeds its own organizer under a unique
/// email and slug, and an organizer only ever sees their own data, which is the
/// same approach the browser suite takes and for the same reason. Test
/// parallelisation is off (see <see cref="SqlServerCollection"/>), so nothing
/// runs alongside the concurrency test that could disturb what it measures.
/// </summary>
public sealed class SqlServerDatabaseFixture : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        SqlServerTestDatabase.AssertIsolated();

        // Dropped first rather than reused: a run must not be able to pass
        // because of rows a previous run left behind.
        await SqlServerTestDatabase.DropIfExistsAsync();
        await SqlServerTestDatabase.CreateEmptyAsync();

        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        // The runtime half of the isolation proof, and the counterpart to the
        // browser suite's "the demo organizer cannot sign in" check. The
        // configured name having been verified only proves what *we* asked for;
        // a freshly-migrated database with no organizers in it proves what the
        // server actually gave us. The developer's database has plenty.
        var organizers = await db.Organizers.CountAsync();
        if (organizers != 0)
        {
            throw new InvalidOperationException(
                $"Expected a freshly created, empty {SqlServerTestDatabase.Name}, but it already " +
                $"contains {organizers} organizers. Refusing to run - this is not a throwaway database.");
        }
    }

    public Task DisposeAsync() => SqlServerTestDatabase.DropIfExistsAsync();

    /// <summary>A context on the test database, outside any web host - for seeding and for asserting.</summary>
    public static BookingTrackerDbContext CreateContext()
        => new(new DbContextOptionsBuilder<BookingTrackerDbContext>()
            .UseSqlServer(SqlServerTestDatabase.ConnectionString)
            .Options);
}

/// <summary>
/// Every test class in this assembly joins this collection, so they share one
/// migrated database and run one at a time.
///
/// <c>DisableParallelization</c> is the same call Playwright's <c>workers: 1</c>
/// makes: one database is shared regardless, and a single worker is what keeps a
/// concurrency failure reproducible rather than blamed on an unrelated test
/// holding a lock.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerDatabaseFixture>
{
    public const string Name = "sql-server";
}
