using System.Diagnostics;
using BookingTracker.Infrastructure.Persistence;
using BookingTracker.MigrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace BookingTracker.MigrationTests.Rollback;

/// <summary>
/// Builds the one thing this suite is about: a real SQL Server database at the
/// CURRENT schema, holding real data, which is then rolled back one migration at
/// a time and brought forward again.
///
/// The sequence, once, for the whole assembly:
///
///   1. drop and recreate BookingTracker_MigrationDownTests
///   2. <c>db.Database.MigrateAsync()</c> - the production call, all 17 migrations
///   3. prove at runtime that it is the latest schema AND a throwaway database
///   4. seed representative data through the real domain entities
///   5. capture <see cref="Snapshot"/> - what the database held BEFORE any rollback
///   6. for each of the sixteen migrations above InitialCreate, NEWEST FIRST:
///        <c>IMigrator.MigrateAsync(previousMigrationId)</c>, timed, then read the
///        schema and the surviving rows back
///   7. at InitialCreate, capture what survived
///   8. <c>db.Database.MigrateAsync()</c> again - the round trip back to latest
///
/// <b>Step 6 uses EF's own migrator with a target, never hand-written SQL.</b>
/// Reimplementing what a Down migration does would prove only that the test
/// agrees with itself; this executes the actual <c>Down()</c> bodies, which is the
/// whole point of the phase.
///
/// <b>Failures are caught and stored, not thrown</b>, exactly as
/// <c>LegacyDatabaseFixture</c> does. A Down migration that cannot run against
/// populated data is the most valuable finding this suite can produce, and it must
/// be reported by the test whose name says so - with every measurement gathered up
/// to that point intact - rather than as a fixture crash on all of them at once.
/// </summary>
public sealed class RollbackFixture : IAsyncLifetime
{
    /// <summary>
    /// One executed Down migration, and what it did.
    ///
    /// The four "unexpectedly..." lists are the step's declared expectations
    /// evaluated AT THE TIME IT RAN, which they have to be: by the end of the
    /// rollback most of these tables no longer exist, so a test asking afterwards
    /// could not tell "this Down removed it" from "a later Down removed it".
    /// </summary>
    public sealed record DownStep(
        MigrationOrder.RollbackStep Step,
        double ElapsedMs,
        Exception? Failure,
        IReadOnlyList<string> AppliedAfter,
        IReadOnlyList<string> TablesAfter,
        IReadOnlyDictionary<string, int> RowCountsAfter,
        IReadOnlyList<string> UnexpectedlyPresentTables,
        IReadOnlyList<string> UnexpectedlyPresentColumns,
        IReadOnlyList<string> UnexpectedlyPresentIndexes,
        IReadOnlyList<string> UnexpectedlyMissingIndexes,
        IReadOnlyList<string> Orphans)
    {
        public bool Succeeded => Failure is null;
        public string Name => Step.Name;

        public bool SchemaMatchesDeclaration =>
            UnexpectedlyPresentTables.Count == 0 && UnexpectedlyPresentColumns.Count == 0 &&
            UnexpectedlyPresentIndexes.Count == 0 && UnexpectedlyMissingIndexes.Count == 0;
    }

    public LatestSchemaData.Dates Dates { get; } = LatestSchemaData.ResolveDates();

    /// <summary>What the seed produced. Null only if the build failed before seeding.</summary>
    public LatestSchemaData.Seeded Seeded { get; private set; } = default!;

    /// <summary>The database as it stood immediately before the first Down migration.</summary>
    public PreRollbackSnapshot Snapshot { get; private set; } = default!;

    /// <summary>One entry per executed Down migration, newest first.</summary>
    public IReadOnlyList<DownStep> Steps { get; private set; } = [];

    /// <summary>Null when every Down migration succeeded.</summary>
    public Exception? RollbackFailure => Steps.FirstOrDefault(s => !s.Succeeded)?.Failure;

    /// <summary>Set if the fixture could not even reach the rollback - a build or seed failure.</summary>
    public Exception? SetupFailure { get; private set; }

    /// <summary>Null when the round trip back to the latest schema succeeded.</summary>
    public Exception? RoundTripFailure { get; private set; }

    public double RoundTripElapsedMs { get; private set; }

    /// <summary>Migration ids applied once the rollback reached InitialCreate.</summary>
    public IReadOnlyList<string> AppliedAtInitialCreate { get; private set; } = [];

    /// <summary>Tables standing once the rollback reached InitialCreate.</summary>
    public IReadOnlyList<string> TablesAtInitialCreate { get; private set; } = [];

    /// <summary>Row counts of the four surviving tables at InitialCreate.</summary>
    public IReadOnlyDictionary<string, int> RowCountsAtInitialCreate { get; private set; }
        = new Dictionary<string, int>();

    /// <summary>Migration ids applied after the round trip - must be all 17 again.</summary>
    public IReadOnlyList<string> AppliedAfterRoundTrip { get; private set; } = [];

    /// <summary>Migrations EF still considers pending after the round trip - must be empty.</summary>
    public IReadOnlyList<string> PendingAfterRoundTrip { get; private set; } = [];

    /// <summary>Row counts after the round trip, so "the tables came back empty" is visible.</summary>
    public IReadOnlyDictionary<string, int> RowCountsAfterRoundTrip { get; private set; }
        = new Dictionary<string, int>();

    public async Task InitializeAsync()
    {
        DownTestDatabase.AssertIsolated();

        try
        {
            await DownTestDatabase.DropIfExistsAsync();
            await DownTestDatabase.CreateEmptyAsync();

            await BuildLatestSchemaAsync();
            await AssertDatabaseIsLatestAsync();
            await AssertDatabaseIsThrowawayAsync();

            await using (var db = CreateContext())
            {
                Seeded = await LatestSchemaData.SeedAsync(db, Dates);
            }

            Snapshot = await PreRollbackSnapshot.CaptureAsync();
        }
        catch (Exception exception)
        {
            SetupFailure = exception;
            return;
        }

        await RollBackAsync();
        await RoundTripAsync();
    }

    public Task DisposeAsync() => DownTestDatabase.DropIfExistsAsync();

    /// <summary>A context on the rollback database, outside any web host.</summary>
    public static BookingTrackerDbContext CreateContext()
        => new(new DbContextOptionsBuilder<BookingTrackerDbContext>()
            .UseSqlServer(DownTestDatabase.ConnectionString, sql => sql.CommandTimeout(600))
            .Options);

    // ---- the three migration phases ------------------------------------------

    private static async Task BuildLatestSchemaAsync()
    {
        await using var db = CreateContext();

        // The production call, verbatim - the same one Program.cs makes on startup
        // in Development. The schema is never hand-built.
        await db.Database.MigrateAsync();
    }

    /// <summary>
    /// The sixteen Down migrations, newest first, each landing on the migration
    /// below it. Stops at the first failure: continuing past one would be rolling
    /// back over a schema whose level is no longer known, and every measurement
    /// after it would describe something other than the migration it names.
    /// </summary>
    private async Task RollBackAsync()
    {
        var steps = new List<DownStep>();

        foreach (var step in MigrationOrder.Path)
        {
            Exception? failure = null;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                await using var db = CreateContext();
                var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();

                // EF's own migrator, told where to stop. Migrating TO the migration
                // below `step` is what executes `step`'s Down() - and nothing else.
                await migrator.MigrateAsync(step.Target);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            stopwatch.Stop();

            steps.Add(new DownStep(
                step,
                stopwatch.Elapsed.TotalMilliseconds,
                failure,
                await SafeAppliedAsync(),
                await SafeTablesAsync(),
                await SurvivingRowCountsAsync(),
                failure is null ? await StillPresentTablesAsync(step) : [],
                failure is null ? await StillPresentColumnsAsync(step) : [],
                failure is null ? await StillPresentIndexesAsync(step) : [],
                failure is null ? await MissingIndexesAsync(step) : [],
                failure is null ? await OrphansAsync() : []));

            if (failure is not null) break;
        }

        Steps = steps;

        if (RollbackFailure is not null) return;

        AppliedAtInitialCreate = await DownTestDatabase.Schema.AppliedMigrationsAsync();
        TablesAtInitialCreate = await DownTestDatabase.Schema.TablesAsync();
        RowCountsAtInitialCreate = await SurvivingRowCountsAsync();
    }

    /// <summary>
    /// Back up to the latest schema, with the same production call that built it.
    /// This is the SCHEMA round trip and is deliberately not called a data round
    /// trip: the tables the rollback dropped come back empty, because dropping a
    /// table destroys its rows and no migration can invent them again.
    /// </summary>
    private async Task RoundTripAsync()
    {
        if (SetupFailure is not null || RollbackFailure is not null) return;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await using var db = CreateContext();
            await db.Database.MigrateAsync();

            PendingAfterRoundTrip = (await db.Database.GetPendingMigrationsAsync()).ToList();
        }
        catch (Exception exception)
        {
            RoundTripFailure = exception;
        }
        stopwatch.Stop();

        RoundTripElapsedMs = stopwatch.Elapsed.TotalMilliseconds;

        if (RoundTripFailure is not null) return;

        AppliedAfterRoundTrip = await DownTestDatabase.Schema.AppliedMigrationsAsync();

        var counts = new Dictionary<string, int>();
        foreach (var table in PreRollbackSnapshot.CountedTables)
        {
            counts[table] = await DownTestDatabase.Schema.RowCountAsync(table);
        }
        RowCountsAfterRoundTrip = counts;
    }

    // ---- runtime proofs ------------------------------------------------------

    /// <summary>
    /// The counterpart to <c>LegacyDatabaseFixture</c>'s "prove it really is old".
    /// Here the risk is the mirror image: a database that is NOT at the latest
    /// schema would make every rollback step below roll back something other than
    /// what it claims, and the suite would pass while proving nothing.
    /// </summary>
    private static async Task AssertDatabaseIsLatestAsync()
    {
        await using var db = CreateContext();

        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count != 0)
        {
            throw new InvalidOperationException(
                $"Expected the database to be fully migrated, but EF still considers {pending.Count} " +
                $"migration(s) pending: {string.Join(", ", pending)}.");
        }

        var applied = await DownTestDatabase.Schema.AppliedMigrationsAsync();
        if (!applied.SequenceEqual(MigrationOrder.All))
        {
            throw new InvalidOperationException(
                "The applied migrations do not match MigrationOrder.All. Expected " +
                $"{MigrationOrder.All.Count} in order; found {applied.Count}: {string.Join(", ", applied)}.");
        }

        // Three facts that can only be true of the latest schema, so a database
        // left at some intermediate level by a previous run is refused rather
        // than silently measured.
        if (await DownTestDatabase.Schema.ColumnAsync("AvailabilityExceptions", "EndDate") is null)
            throw new InvalidOperationException("AvailabilityExceptions.EndDate is missing - this is not the latest schema.");

        if (await DownTestDatabase.Schema.IndexAsync("BookingSessions", "IX_BookingSessions_BookingPageId_Status_SelectedDate") is null)
            throw new InvalidOperationException("The conflict index is missing - this is not the latest schema.");

        if (!await DownTestDatabase.Schema.TableExistsAsync("AvailabilityOverrides"))
            throw new InvalidOperationException("AvailabilityOverrides is missing - this is not the latest schema.");
    }

    private static async Task AssertDatabaseIsThrowawayAsync()
    {
        var organizers = await DownTestDatabase.Schema.RowCountAsync("Organizers");
        if (organizers != 0)
        {
            throw new InvalidOperationException(
                $"Expected a freshly created, empty {DownTestDatabase.Name}, but it already contains " +
                $"{organizers} organizers. Refusing to run - this is not a throwaway database.");
        }
    }

    // ---- readers that must not throw mid-rollback -----------------------------

    /// <summary>
    /// Row counts of the four tables that exist at EVERY level of the rollback
    /// path - the ones InitialCreate creates. Everything else is dropped part-way
    /// through, so counting it would throw rather than report.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, int>> SurvivingRowCountsAsync()
    {
        var counts = new Dictionary<string, int>();
        foreach (var table in MigrationOrder.InitialCreateTables)
        {
            counts[table] = await DownTestDatabase.Schema.RowCountAsync(table);
        }
        return counts;
    }

    // ---- the step's own declaration, checked where it is still checkable ------

    private static async Task<IReadOnlyList<string>> StillPresentTablesAsync(MigrationOrder.RollbackStep step)
    {
        var present = new List<string>();
        foreach (var table in step.TablesDropped)
        {
            if (await DownTestDatabase.Schema.TableExistsAsync(table)) present.Add(table);
        }
        return present;
    }

    private static async Task<IReadOnlyList<string>> StillPresentColumnsAsync(MigrationOrder.RollbackStep step)
    {
        var present = new List<string>();
        foreach (var (table, column) in step.ColumnsDropped)
        {
            if (await DownTestDatabase.Schema.ColumnAsync(table, column) is not null) present.Add($"{table}.{column}");
        }
        return present;
    }

    private static async Task<IReadOnlyList<string>> StillPresentIndexesAsync(MigrationOrder.RollbackStep step)
    {
        var present = new List<string>();
        foreach (var (table, index) in step.IndexesDropped)
        {
            if (await DownTestDatabase.Schema.IndexAsync(table, index) is not null) present.Add($"{table}.{index}");
        }
        return present;
    }

    private static async Task<IReadOnlyList<string>> MissingIndexesAsync(MigrationOrder.RollbackStep step)
    {
        var missing = new List<string>();
        foreach (var (table, index) in step.IndexesRestored)
        {
            if (await DownTestDatabase.Schema.IndexAsync(table, index) is null) missing.Add($"{table}.{index}");
        }
        return missing;
    }

    /// <summary>
    /// The three foreign keys that exist at EVERY level of the rollback path, so
    /// the same check is meaningful after all sixteen steps. SQL Server enforces
    /// these continuously, so a non-zero answer would mean an FK was recreated
    /// without validation rather than that a row escaped - which is exactly the
    /// kind of thing a DropIndex/CreateIndex pair could get wrong quietly.
    /// </summary>
    private static async Task<IReadOnlyList<string>> OrphansAsync()
    {
        var orphans = new List<string>();

        foreach (var (child, column, parent) in new[]
        {
            ("BookingPages", "OrganizerId", "Organizers"),
            ("BookingSessions", "BookingPageId", "BookingPages"),
            ("BookingSessionEvents", "SessionId", "BookingSessions"),
        })
        {
            var count = await DownTestDatabase.Schema.OrphanedChildRowsAsync(child, column, parent);
            if (count != 0) orphans.Add($"{child}.{column} -> {parent}: {count} orphan(s)");
        }

        return orphans;
    }

    private static async Task<IReadOnlyList<string>> SafeAppliedAsync()
    {
        try { return await DownTestDatabase.Schema.AppliedMigrationsAsync(); }
        catch (Exception) { return []; }
    }

    private static async Task<IReadOnlyList<string>> SafeTablesAsync()
    {
        try { return await DownTestDatabase.Schema.TablesAsync(); }
        catch (Exception) { return []; }
    }
}

/// <summary>
/// One database, one rollback, shared by every test in the suite - and
/// parallelisation off.
///
/// Rolling back sixteen migrations and coming back up takes seconds and can only
/// be done once per database, so every assertion is made against the same run.
/// <c>DisableParallelization</c> additionally keeps this away from
/// <c>MigrationCollection</c>, which is building and migrating its own database
/// on the same server.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RollbackCollection : ICollectionFixture<RollbackFixture>
{
    public const string Name = "migration-rollback";
}
