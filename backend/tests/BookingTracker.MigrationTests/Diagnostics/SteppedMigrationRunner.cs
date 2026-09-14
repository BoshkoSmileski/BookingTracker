using System.Diagnostics;
using BookingTracker.Infrastructure.Persistence;
using BookingTracker.MigrationTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace BookingTracker.MigrationTests.Diagnostics;

/// <summary>
/// Applies the migrations in <see cref="LegacySchema.Applied"/> ONE AT A TIME,
/// timing and instrumenting each, so a slow upgrade names the migration that was
/// slow instead of reporting one number for nine migrations.
///
/// <b>The same EF mechanism the correctness suite uses.</b>
/// <c>LegacyDatabaseFixture</c> calls <c>IMigrator.MigrateAsync(target)</c> once
/// to stop at the baseline and then <c>Database.MigrateAsync()</c> to go the rest
/// of the way; this calls the first of those nine times, once per target. The
/// migrations are untouched, no migration SQL is hand-run, and nothing here
/// reimplements what a migration does - which is the rule this suite follows,
/// and the reason a measurement taken here describes the real upgrade.
///
/// <b>The connection is opened explicitly and held open, and that is
/// load-bearing.</b> EF closes a connection it opened itself as soon as a command
/// finishes, which would end the session - and <c>sys.dm_exec_sessions</c>'
/// counters are per session, so a before/after delta across a closed session is
/// meaningless, and the observer would be watching a spid that no longer exists.
/// Opening it externally makes EF reference-count rather than close, so one spid
/// serves the whole migration.
/// </summary>
public static class SteppedMigrationRunner
{
    public sealed record Step(
        string MigrationId,
        double ElapsedMs,
        MigrationObserver.SessionCounters Cost,
        MigrationObserver.Observation Observed,
        ConcurrentReader.Result Reader,
        double BaselineReadMs,
        Exception? Failure)
    {
        public string Name => MigrationId[(MigrationId.IndexOf('_') + 1)..];
        public bool Succeeded => Failure is null;

        /// <summary>Peak transaction log the migration accumulated, in MB.</summary>
        public double LogMb => Observed.PeakLogBytes / 1024d / 1024d;

        /// <summary>How much worse an ordinary application read got while this ran.</summary>
        public double ReaderSlowdown => BaselineReadMs <= 0 ? 0 : Reader.MaxMs / BaselineReadMs;
    }

    /// <summary>
    /// Which application read runs alongside each migration.
    ///
    /// The availability read for the exception backfill, because that is the
    /// table it rewrites; the conflict read for everything else, because
    /// BookingSessions is the table every other migration in the range touches
    /// and the one the product's hottest query depends on. A probe pointed at a
    /// table the migration never mentions would report "no blocking" for the
    /// wrong reason.
    /// </summary>
    private static string ReadFor(string migrationId)
        => migrationId.Contains("AddAvailabilityExceptionEndDate", StringComparison.Ordinal)
            ? ConcurrentReader.AvailabilityShapedRead
            : ConcurrentReader.ConflictShapedRead;

    public static async Task<IReadOnlyList<Step>> RunAsync(
        LegacyVolumeSeeder.Result seed, Action<string> log)
    {
        var steps = new List<Step>();

        foreach (var migrationId in LegacySchema.Applied)
        {
            var step = await RunOneAsync(migrationId, seed, log);
            steps.Add(step);

            if (!step.Succeeded)
            {
                log($"  !! {step.Name} FAILED: {step.Failure!.Message}");
                break;
            }
        }

        return steps;
    }

    private static async Task<Step> RunOneAsync(string migrationId, LegacyVolumeSeeder.Result seed, Action<string> log)
    {
        VolumeDiagnosticDatabase.AssertIsolated();

        await using var reader = await ConcurrentReader.OpenAsync(ReadFor(migrationId), seed.ProbeOrganizerId, seed.ProbeDate);
        var baselineReadMs = await reader.WarmAndTimeOnceAsync();

        await using var observer = await MigrationObserver.OpenAsync();

        await using var db = CreateContext();
        await db.Database.OpenConnectionAsync();

        var spid = await ReadSessionIdAsync(db);
        var before = await observer.ReadCountersAsync(spid);

        reader.Start();
        await observer.StartAsync(spid, reader.SessionId);

        Exception? failure = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
            await migrator.MigrateAsync(migrationId);
        }
        catch (Exception exception)
        {
            // Stored rather than thrown, exactly as LegacyDatabaseFixture does:
            // a migration that cannot cope with this volume is the single most
            // valuable finding this diagnostic can produce, and it must be
            // REPORTED with its measurements rather than surfacing as a crash
            // that loses every number gathered so far.
            failure = exception;
        }
        stopwatch.Stop();

        var observed = await observer.StopAsync();
        var readerResult = await reader.StopAsync();
        var after = failure is null ? await observer.ReadCountersAsync(spid) : before;

        var step = new Step(
            migrationId, stopwatch.Elapsed.TotalMilliseconds, after - before,
            observed, readerResult, baselineReadMs, failure);

        log($"  {step.Name,-46} {step.ElapsedMs,9:N0} ms  " +
            $"reads {step.Cost.LogicalReads,10:N0}  writes {step.Cost.Writes,8:N0}  " +
            $"log {step.LogMb,7:N1} MB  " +
            $"{(observed.SchemaModificationLock ? "Sch-M" : "  -  ")}  " +
            $"reader max {readerResult.MaxMs,8:N1} ms{(observed.ReaderBlockedByMigration ? "  BLOCKED" : string.Empty)}");

        return step;
    }

    private static async Task<short> ReadSessionIdAsync(BookingTrackerDbContext db)
    {
        var connection = (SqlConnection)db.Database.GetDbConnection();
        await using var command = new SqlCommand("SELECT @@SPID;", connection);
        return (short)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>A context on the diagnostic database, outside any web host.</summary>
    public static BookingTrackerDbContext CreateContext()
        => new(new DbContextOptionsBuilder<BookingTrackerDbContext>()
            .UseSqlServer(VolumeDiagnosticDatabase.ConnectionString, sql => sql.CommandTimeout(1800))
            .Options);
}
