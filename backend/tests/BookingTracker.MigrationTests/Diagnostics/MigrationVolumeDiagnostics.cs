using BookingTracker.MigrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace BookingTracker.MigrationTests.Diagnostics;

/// <summary>
/// What the migrations actually cost on a database that already holds a
/// realistic number of rows.
///
/// <b>Why this exists.</b> The correctness suite proved the migrations are CORRECT on a
/// populated database - every legacy row and id survives, the backfill is
/// applied, the narrowed column keeps its contents. It proved it on roughly
/// fifty rows, which is the size at which every migration finishes in a
/// millisecond and holds every lock for less than that. Correctness at fifty rows
/// says nothing about whether the same upgrade is SAFE TO DEPLOY on a database
/// with half a million booking sessions, and two migrations in the range have a
/// specific reason to be asked:
///
///   AddAvailabilityExceptionEndDate      an UNFILTERED UPDATE over every row of
///                                        AvailabilityExceptions, inside the
///                                        migration's transaction
///   NarrowBookingConflictLockFootprint   drops and rebuilds an index on
///                                        BookingSessions, the largest table
///
/// A third earns its place by measurement rather than by the brief:
/// <c>AddBookingSessionMessageMaxLength</c> is an ALTER COLUMN narrowing
/// nvarchar(max) to nvarchar(2000) on that same largest table, which is a
/// size-of-data operation. It is measured for the same reason - the point of the
/// phase is to find out, not to confirm.
///
/// <b>Every migration in the range is timed, not only those three</b>, because
/// "the other six create empty tables and cost nothing at any volume" is a claim
/// this should demonstrate rather than assert.
///
/// <b>These MEASURE; they do not guard.</b> Nothing here asserts a duration -
/// a timing threshold on a developer machine is a flaky test, not a regression
/// guard. The one thing asserted is that every migration SUCCEEDED, because a
/// migration that cannot cope with the volume is the single most valuable finding
/// this file can produce and must not be reported as a passing measurement.
/// </summary>
[Collection(VolumeDiagnosticCollection.Name)]
public class MigrationVolumeDiagnostics(ITestOutputHelper output)
{
    [DiagnosticFact]
    public Task Small() => MeasureAsync(VolumeProfile.Small);

    [DiagnosticFact]
    public Task Medium() => MeasureAsync(VolumeProfile.Medium);

    /// <summary>
    /// Its own test rather than a third iteration inside one, so it can be
    /// excluded with a name filter on a machine where half a million rows is
    /// impractical without losing the other two levels.
    /// </summary>
    [DiagnosticFact]
    public Task Large() => MeasureAsync(VolumeProfile.Large);

    // -------------------------------------------------------------------------

    private async Task MeasureAsync(VolumeProfile profile)
    {
        VolumeDiagnosticDatabase.AssertIsolated();

        try
        {
            await BuildLegacyDatabaseAsync();

            output.WriteLine($"=== {profile}");
            output.WriteLine($"    database    {VolumeDiagnosticDatabase.Name}");
            output.WriteLine($"    baseline    {LegacySchema.Baseline}");
            // Both SERVERPROPERTY values are sql_variant, which will not implicitly
            // convert to a string - CAST each rather than letting CONCAT try.
            output.WriteLine($"    engine      {await ScalarAsync<string>(
                "SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(64)) + N' ' + CAST(SERVERPROPERTY('Edition') AS nvarchar(128));")}");
            output.WriteLine($"    recovery    {await VolumeDiagnosticDatabase.RecoveryModelAsync()}");
            output.WriteLine(string.Empty);

            output.WriteLine("--- seeding legacy rows");
            var seed = await LegacyVolumeSeeder.SeedAsync(profile, output.WriteLine);
            output.WriteLine(
                $"    seeded in {seed.Elapsed.TotalSeconds:N1}s; allocated {seed.DataSizeMb:N0} MB");
            output.WriteLine(string.Empty);

            output.WriteLine("--- row counts before the upgrade");
            foreach (var table in new[] { "Organizers", "BookingPages", "AvailabilityExceptions", "BookingSessions" })
            {
                output.WriteLine($"    {table,-24} {await ScalarAsync<long>($"SELECT COUNT_BIG(*) FROM [{table}];"),12:N0}");
            }
            output.WriteLine(string.Empty);

            output.WriteLine("--- applying migrations one at a time, with a concurrent application-shaped read");
            var steps = await SteppedMigrationRunner.RunAsync(seed, output.WriteLine);
            output.WriteLine(string.Empty);

            WriteSummary(profile, steps);
            WriteLockDetail(steps);

            // The only assertion, and it is about correctness rather than speed.
            var failed = steps.Where(s => !s.Succeeded).ToArray();
            Assert.True(
                failed.Length == 0,
                failed.Length == 0
                    ? string.Empty
                    : $"Migration(s) failed at {profile.Label}: " +
                      string.Join("; ", failed.Select(f => $"{f.Name}: {f.Failure!.Message}")));

            Assert.Equal(LegacySchema.Applied.Count, steps.Count);
        }
        finally
        {
            // Dropped whatever happened, so a failed level cannot leave several
            // hundred megabytes behind or make the next level measure a database
            // that is already migrated.
            await VolumeDiagnosticDatabase.DropIfExistsAsync();
        }
    }

    // ---- output -------------------------------------------------------------

    private void WriteSummary(VolumeProfile profile, IReadOnlyList<SteppedMigrationRunner.Step> steps)
    {
        output.WriteLine($"=== SUMMARY - {profile.Label}");
        output.WriteLine("""

            migration                                       elapsed    cpu   logical reads     writes    log MB  Sch-M  reader max  blocked
            ---------------------------------------------  --------  -----  --------------  ---------  --------  -----  ----------  -------
            """);

        foreach (var step in steps)
        {
            output.WriteLine(
                $"{step.Name,-45}  {step.ElapsedMs,6:N0}ms  {step.Cost.CpuMs,4:N0}ms  " +
                $"{step.Cost.LogicalReads,14:N0}  {step.Cost.Writes,9:N0}  {step.LogMb,8:N1}  " +
                $"{(step.Observed.SchemaModificationLock ? "yes" : "no"),-5}  " +
                $"{step.Reader.MaxMs,8:N1}ms  {(step.Observed.ReaderBlockedByMigration ? "YES" : "no"),-7}");
        }

        var total = steps.Sum(s => s.ElapsedMs);
        output.WriteLine(string.Empty);
        output.WriteLine($"    whole upgrade: {total:N0} ms ({total / 1000:N1} s) across {steps.Count} migrations");
        output.WriteLine(string.Empty);
    }

    private void WriteLockDetail(IReadOnlyList<SteppedMigrationRunner.Step> steps)
    {
        output.WriteLine("=== LOCK AND BLOCKING EVIDENCE (peak per migration, continuous sampling)");

        foreach (var step in steps)
        {
            output.WriteLine(string.Empty);
            output.WriteLine($"  {step.Name}  ({step.Observed.Samples} samples)");

            if (step.Observed.Locks.Count == 0)
            {
                output.WriteLine("      no locks observed - the migration finished inside the sampling interval");
            }
            else
            {
                // Schema-modification locks first and ALWAYS, never truncated away
                // by the "top N" cut. Which OBJECT a Sch-M was taken on is the
                // single most decision-relevant fact this file produces, and it is
                // routinely not in the top eight by count: a Sch-M is one lock,
                // while the catalog churn beside it is dozens. Listing it by count
                // is how "Sch-M on BookingSessions" ends up invisible underneath
                // "KEY X on sysrscols x8".
                foreach (var peak in step.Observed.Locks.Where(l => l.Mode == "Sch-M"))
                {
                    output.WriteLine($"      >> {peak}");
                }

                foreach (var peak in step.Observed.Locks.Where(l => l.Mode != "Sch-M").Take(6))
                {
                    output.WriteLine($"      {peak}");
                }
            }

            if (step.Observed.WaitTypes.Count > 0)
            {
                output.WriteLine($"      waits: {string.Join(", ", step.Observed.WaitTypes)}");
            }

            output.WriteLine(
                $"      concurrent read: baseline {step.BaselineReadMs:N1} ms, during migration {step.Reader}" +
                (step.ReaderSlowdown > 1 ? $" ({step.ReaderSlowdown:N1}x baseline)" : string.Empty));

            if (step.Observed.ReaderBlockedByMigration)
            {
                output.WriteLine(
                    $"      BLOCKED: the reader waited on this migration, up to {step.Observed.MaxReaderWaitMs} ms");
                foreach (var wait in step.Observed.ReaderWaitTypes)
                {
                    output.WriteLine($"        {wait}");
                }
            }
        }

        output.WriteLine(string.Empty);
        output.WriteLine("""
            Lines marked >> are schema-modification locks. Sch-M is incompatible with
            every other lock mode INCLUDING shared, so for as long as one is held the
            object is unavailable to the application.

            Read the OBJECT it names, not the "yes" in the summary column. A Sch-M on
            a table the migration is CREATING blocks nothing - nothing else can be
            using a table that does not exist yet. A Sch-M on BookingSessions is the
            one that costs, and a migration that only creates a table can still take
            one on BookingSessions if it adds a foreign key REFERENCING it.

            The reader columns are what that cost in practice: a real,
            application-shaped query running throughout, and whether SQL Server itself
            reported it as waiting on this migration (blocking_session_id), rather
            than the wait being inferred from latency.
            """);
    }

    // ---- building the legacy database ---------------------------------------

    /// <summary>
    /// Drops, recreates and migrates the diagnostic database to
    /// <see cref="LegacySchema.Baseline"/> - and then proves at runtime that it
    /// really is old and really is a throwaway, exactly as
    /// <c>LegacyDatabaseFixture</c> does and for exactly the same reason: a stale
    /// pooled connection or a leftover database would leave it already fully
    /// migrated, and every migration below would then report a near-zero timing
    /// while measuring nothing at all.
    /// </summary>
    private async Task BuildLegacyDatabaseAsync()
    {
        await VolumeDiagnosticDatabase.DropIfExistsAsync();
        await VolumeDiagnosticDatabase.CreateEmptyAsync();

        await using (var db = SteppedMigrationRunner.CreateContext())
        {
            var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrator.MigrateAsync(LegacySchema.Baseline);
        }

        var applied = await ScalarAsync<string>(
            "SELECT TOP 1 MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC;");
        if (applied != LegacySchema.Baseline)
        {
            throw new InvalidOperationException(
                $"Expected the database to stop at {LegacySchema.Baseline}, but the last applied migration " +
                $"is '{applied ?? "(none)"}'. Refusing to measure a database that is not at the legacy schema.");
        }

        var endDate = await ScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('AvailabilityExceptions') AND name = 'EndDate';");
        if (endDate != 0)
        {
            throw new InvalidOperationException(
                "AvailabilityExceptions.EndDate already exists, so this database is not at the legacy schema.");
        }

        var organizers = await ScalarAsync<int>("SELECT COUNT(*) FROM [Organizers];");
        if (organizers != 0)
        {
            throw new InvalidOperationException(
                $"Expected a freshly created, empty {VolumeDiagnosticDatabase.Name}, but it already contains " +
                $"{organizers} organizers. Refusing to run - this is not a throwaway database.");
        }
    }

    private static async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = await VolumeDiagnosticDatabase.OpenAsync();
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 300 };
        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default! : (T)Convert.ChangeType(result, typeof(T))!;
    }
}

/// <summary>
/// Its own collection, with parallelisation off.
///
/// Two reasons, both real: the three volume levels each drop and recreate the
/// same database, so they cannot overlap with each other; and this must never run
/// alongside <c>MigrationCollection</c>, whose fixture is building and migrating
/// its own database on the same server - a four-core Express instance cannot do
/// both and have either measurement mean anything.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class VolumeDiagnosticCollection
{
    public const string Name = "migration-volume-diagnostics";
}
