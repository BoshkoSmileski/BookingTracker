using BookingTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace BookingTracker.MigrationTests.Infrastructure;

/// <summary>
/// Builds the one thing this suite is about: a real SQL Server database that
/// holds real rows written under an OLDER schema, and then upgrades it with the
/// real EF Core migration mechanism.
///
/// The sequence, once, for the whole assembly:
///
///   1. drop and recreate BookingTracker_MigrationTests
///   2. migrate to <see cref="LegacySchema.Baseline"/> AND NO FURTHER
///   3. prove at runtime that the database really is at the old schema
///   4. prove at runtime that it is a throwaway database (no organizers)
///   5. seed representative legacy data
///   6. read that data back as <see cref="Snapshot"/>
///   7. <c>db.Database.MigrateAsync()</c> - the production call, unmodified
///
/// **Step 2 is the whole point.** Every other SQL-backed suite in this
/// repository migrates an EMPTY database to the latest schema, which proves the
/// schema can be created and nothing about upgrading one that already holds
/// rows. <see cref="IMigrator.MigrateAsync"/> with an explicit target is EF's
/// own supported way to stop part-way; the migrations themselves are untouched,
/// and step 7 runs the same <c>MigrateAsync()</c> that <c>Program.cs</c> calls
/// in Development.
///
/// **Step 3 exists because step 2 could silently do nothing.** A stale pooled
/// connection, a database left behind by a previous run, or a mistyped target
/// would each leave a database at the latest schema - and every assertion in
/// this suite would then pass while proving exactly nothing. So the fixture
/// checks the three facts that can only be true of the old schema:
/// AvailabilityExceptions has no EndDate, BookingSessions.Message is still
/// nvarchar(max), and the EmailNotifications table does not exist yet.
///
/// The migration failure, if there is one, is CAUGHT and stored rather than
/// thrown, so a broken migration is reported by the test whose name says that is
/// what broke instead of as a fixture crash on every test at once.
/// </summary>
public sealed class LegacyDatabaseFixture : IAsyncLifetime
{
    /// <summary>The dates the seed used. Resolved once, relative to now.</summary>
    public LegacyData.Dates Dates { get; } = LegacyData.ResolveDates();

    /// <summary>What the database held immediately before <c>MigrateAsync()</c> ran.</summary>
    public LegacySnapshot Snapshot { get; private set; } = default!;

    /// <summary>Null when the upgrade succeeded. Asserted by <c>MigrationAppliesToAPopulatedDatabase</c>.</summary>
    public Exception? MigrationFailure { get; private set; }

    /// <summary>Migrations EF had recorded as applied while the database was still legacy.</summary>
    public IReadOnlyList<string> MigrationsBeforeUpgrade { get; private set; } = [];

    public async Task InitializeAsync()
    {
        MigrationTestDatabase.AssertIsolated();

        await MigrationTestDatabase.DropIfExistsAsync();
        await MigrationTestDatabase.CreateEmptyAsync();

        await MigrateToBaselineAsync();
        await AssertDatabaseIsLegacyAsync();
        await AssertDatabaseIsThrowawayAsync();

        await LegacyData.SeedAsync(Dates);
        MigrationsBeforeUpgrade = await SchemaProbe.AppliedMigrationsAsync();
        Snapshot = await LegacySnapshot.CaptureAsync();

        await UpgradeToLatestAsync();
    }

    public Task DisposeAsync() => MigrationTestDatabase.DropIfExistsAsync();

    /// <summary>A context on the migrated database, outside any web host - for reading and asserting.</summary>
    public static BookingTrackerDbContext CreateContext()
        => new(new DbContextOptionsBuilder<BookingTrackerDbContext>()
            .UseSqlServer(MigrationTestDatabase.ConnectionString)
            .Options);

    // ---- the two migration calls --------------------------------------------

    private static async Task MigrateToBaselineAsync()
    {
        await using var db = CreateContext();

        // EF's own migrator, with a target. Not hand-run SQL, and not a
        // reimplementation of what MigrateAsync does - the same code path, told
        // where to stop.
        var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
        await migrator.MigrateAsync(LegacySchema.Baseline);
    }

    private async Task UpgradeToLatestAsync()
    {
        try
        {
            await using var db = CreateContext();

            // The production call, verbatim: this is what Program.cs runs on
            // startup in Development. If a migration in the range cannot cope
            // with the rows that are now present, it fails here.
            await db.Database.MigrateAsync();
        }
        catch (Exception exception)
        {
            MigrationFailure = exception;
        }
    }

    // ---- the runtime proofs --------------------------------------------------

    private static async Task AssertDatabaseIsLegacyAsync()
    {
        var applied = await SchemaProbe.AppliedMigrationsAsync();
        if (applied.Count == 0 || applied[^1] != LegacySchema.Baseline)
        {
            throw new InvalidOperationException(
                $"Expected the database to stop at {LegacySchema.Baseline}, but the last applied " +
                $"migration is '{(applied.Count == 0 ? "(none)" : applied[^1])}'. Refusing to run - " +
                "a database that is already at the latest schema would make every assertion in this " +
                "suite pass while proving nothing.");
        }

        if (await SchemaProbe.ColumnAsync("AvailabilityExceptions", "EndDate") is not null)
        {
            throw new InvalidOperationException(
                "AvailabilityExceptions.EndDate already exists, so this database is not at the legacy schema.");
        }

        var message = await SchemaProbe.ColumnAsync("BookingSessions", "Message")
            ?? throw new InvalidOperationException("BookingSessions.Message is missing - this is not the expected legacy schema.");
        if (message.MaxCharacters != -1)
        {
            throw new InvalidOperationException(
                $"BookingSessions.Message is already nvarchar({message.MaxCharacters}); the legacy schema has nvarchar(max).");
        }

        if (await SchemaProbe.TableExistsAsync("EmailNotifications"))
        {
            throw new InvalidOperationException(
                "The EmailNotifications table already exists, so this database is ahead of the legacy schema.");
        }
    }

    /// <summary>
    /// The counterpart to <c>SqlServerDatabaseFixture</c>'s "zero organizers"
    /// check, and to the browser suite's "the demo organizer cannot sign in".
    /// The configured name having been verified only proves what we ASKED for; a
    /// freshly created database with nothing in it proves what the server
    /// actually gave us. The developer's database has plenty.
    /// </summary>
    private static async Task AssertDatabaseIsThrowawayAsync()
    {
        var organizers = await SchemaProbe.RowCountAsync("Organizers");
        if (organizers != 0)
        {
            throw new InvalidOperationException(
                $"Expected a freshly created, empty {MigrationTestDatabase.Name}, but it already contains " +
                $"{organizers} organizers. Refusing to run - this is not a throwaway database.");
        }
    }
}

/// <summary>
/// Every test class in this assembly joins this collection, so the legacy
/// database is built, seeded and upgraded exactly once and every test asserts
/// against the same migrated result.
///
/// <c>DisableParallelization</c> because there is one database and one
/// migration: two classes running at once would be reading the same rows while
/// one of them writes the post-migration booking.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MigrationCollection : ICollectionFixture<LegacyDatabaseFixture>
{
    public const string Name = "legacy-database-migration";
}
