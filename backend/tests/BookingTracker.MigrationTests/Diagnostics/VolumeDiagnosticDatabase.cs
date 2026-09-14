using Microsoft.Data.SqlClient;

namespace BookingTracker.MigrationTests.Diagnostics;

/// <summary>
/// The one place the volume diagnostic's database is named, and the one place
/// that refuses to run against anything else.
///
/// A FIFTH database, and it has to be. This diagnostic drops and recreates its
/// database three times in one run (once per volume level), builds each one at
/// an OLD migration, and then fills it with up to half a million rows. Pointed
/// at any of the other four it would be the most destructive thing in the
/// repository:
///
///   BookingTracker                  the developer's own data
///   BookingTracker_E2E              the browser suite's, whose global setup
///                                   asserts things about its contents
///   BookingTracker_SqlTests         the SQL Server integration suite's
///   BookingTracker_MigrationTests   the CORRECTNESS migration suite's, which
///                                   this one must not disturb even though the
///                                   two live in the same assembly
///
/// All four are therefore named and refused explicitly rather than merely being
/// "not ours" - the same escalation <see cref="MigrationTestDatabase"/> made
/// when it went from one refused name to three.
/// </summary>
public static class VolumeDiagnosticDatabase
{
    public const string DevelopmentDatabase = "BookingTracker";
    public const string BrowserSuiteDatabase = "BookingTracker_E2E";
    public const string SqlSuiteDatabase = "BookingTracker_SqlTests";
    public const string MigrationSuiteDatabase = "BookingTracker_MigrationTests";

    public const string Name = "BookingTracker_MigrationVolumeDiagnostics";

    /// <summary>
    /// Overridable with MIGRATIONVOLUME_CONNECTION_STRING for a differently-named
    /// local instance, but the database name itself is still checked - the
    /// override exists to point at another *server*, never at another database.
    ///
    /// Deliberately NOT derived from MIGRATIONTESTS_CONNECTION_STRING: reusing
    /// that variable and swapping the catalog would mean one wrong value could
    /// aim both suites at the same database.
    /// </summary>
    public static string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("MIGRATIONVOLUME_CONNECTION_STRING")
        ?? $"Server=localhost\\SQLEXPRESS;Database={Name};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    public static string MasterConnectionString =>
        new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString;

    /// <summary>
    /// Refuses any connection string that does not name <see cref="Name"/>.
    /// Called before the first connection of every step, and again before each
    /// drop.
    /// </summary>
    public static void AssertIsolated()
    {
        var database = new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;

        if (!string.Equals(database, Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"""
                 The migration volume diagnostic refuses to run against database "{database}".
                 It must name "{Name}" - never the development
                 database "{DevelopmentDatabase}", never the browser suite's
                 "{BrowserSuiteDatabase}", never the SQL Server integration suite's
                 "{SqlSuiteDatabase}", and never the migration correctness suite's
                 "{MigrationSuiteDatabase}". Check MIGRATIONVOLUME_CONNECTION_STRING.
                 """);
        }
    }

    public static async Task DropIfExistsAsync()
    {
        AssertIsolated();
        SqlConnection.ClearAllPools();

        // SINGLE_USER WITH ROLLBACK IMMEDIATE for the same reason the other two
        // suites use it: a pooled connection left open by a previous run would
        // otherwise make the drop fail, and the next volume level would then be
        // measured on a database that is already fully migrated - which would
        // report near-zero timings while proving nothing at all.
        await ExecuteOnMasterAsync($"""
            IF DB_ID(N'{Name}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{Name}];
            END
            """);
    }

    /// <summary>
    /// Creates the database with its data and log files pre-sized, and in SIMPLE
    /// recovery.
    ///
    /// Both are deliberate and both are reported in the diagnostic's output,
    /// because both change what the numbers mean:
    ///
    ///  - **Pre-sized files.** An autogrowth stall in the middle of a measured
    ///    migration measures this machine's disk and default growth increment,
    ///    not the migration. Sized past what the largest volume level needs so
    ///    no growth event can land inside a measurement.
    ///  - **SIMPLE recovery.** The log bytes each migration generates are
    ///    reported either way (read from
    ///    <c>sys.dm_tran_database_transactions</c>), and the LOCK window - which
    ///    is what the deployment-risk question is actually about - is unaffected
    ///    by recovery model. What FULL recovery changes is that the log is
    ///    retained until backed up, so a production database in FULL will need
    ///    room for the same number of bytes rather than a different number.
    /// </summary>
    public static async Task CreateEmptyAsync()
    {
        AssertIsolated();

        await ExecuteOnMasterAsync($"""
            IF DB_ID(N'{Name}') IS NULL CREATE DATABASE [{Name}];
            """);

        await ExecuteOnMasterAsync($"""
            ALTER DATABASE [{Name}] SET RECOVERY SIMPLE;
            ALTER DATABASE [{Name}] MODIFY FILE (NAME = N'{Name}', SIZE = 2048MB, FILEGROWTH = 256MB);
            ALTER DATABASE [{Name}] MODIFY FILE (NAME = N'{Name}_log', SIZE = 1024MB, FILEGROWTH = 256MB);
            """);
    }

    /// <summary>What SQL Server says the recovery model actually is, for the report header.</summary>
    public static async Task<string> RecoveryModelAsync()
    {
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT recovery_model_desc FROM sys.databases WHERE name = @name;", connection);
        command.Parameters.AddWithValue("@name", Name);
        return (string?)await command.ExecuteScalarAsync() ?? "(unknown)";
    }

    private static async Task ExecuteOnMasterAsync(string sql)
    {
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 300;
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>An open connection on the diagnostic database, for raw SQL.</summary>
    public static async Task<SqlConnection> OpenAsync()
    {
        AssertIsolated();
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}
