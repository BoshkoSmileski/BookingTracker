using BookingTracker.MigrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace BookingTracker.MigrationTests.Rollback;

/// <summary>
/// The one place the rollback suite's database is named, and the one place that
/// refuses to run against anything else.
///
/// A SIXTH database. That is not enthusiasm for databases - it is what this suite
/// does to the one it is given. It applies every migration, fills the result with
/// real data, and then **runs sixteen Down migrations over it**, which between
/// them drop fourteen tables and twenty-eight columns. Pointed at any of the
/// five others this would be by a wide margin the most destructive thing in the
/// repository, and unlike a failed seed it would not be noticed until something
/// tried to read a table that no longer exists.
///
///   BookingTracker                              the developer's own data
///   BookingTracker_E2E                          the browser suite's
///   BookingTracker_SqlTests                     the SQL Server integration suite's
///   BookingTracker_MigrationTests               the migration CORRECTNESS suite's
///   BookingTracker_MigrationVolumeDiagnostics   the volume diagnostics'
///
/// All five are named and refused explicitly rather than merely being "not ours",
/// continuing the escalation <see cref="MigrationTestDatabase"/> started at three
/// and <c>VolumeDiagnosticDatabase</c> continued at four. The check runs before
/// the first connection is opened and again before every drop, so a wrong value
/// fails while the database it names is still untouched.
/// </summary>
public static class DownTestDatabase
{
    public const string DevelopmentDatabase = "BookingTracker";
    public const string BrowserSuiteDatabase = "BookingTracker_E2E";
    public const string SqlSuiteDatabase = "BookingTracker_SqlTests";
    public const string MigrationSuiteDatabase = "BookingTracker_MigrationTests";
    public const string VolumeDiagnosticDatabase = "BookingTracker_MigrationVolumeDiagnostics";

    public const string Name = "BookingTracker_MigrationDownTests";

    /// <summary>
    /// Overridable with DOWNTESTS_CONNECTION_STRING for a differently-named local
    /// instance, but the database name itself is still checked - the override
    /// exists to point at another *server*, never at another database.
    ///
    /// Its own variable, deliberately not derived from any of the others, so one
    /// wrong value cannot aim two suites at one database.
    /// </summary>
    public static string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("DOWNTESTS_CONNECTION_STRING")
        ?? $"Server=localhost\\SQLEXPRESS;Database={Name};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    public static string MasterConnectionString =>
        new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString;

    public static void AssertIsolated()
    {
        var database = new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;

        if (!string.Equals(database, Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"""
                 The Down-migration suite refuses to run against database "{database}".
                 It must name "{Name}" - never the development database
                 "{DevelopmentDatabase}", never the browser suite's "{BrowserSuiteDatabase}",
                 never the SQL Server integration suite's "{SqlSuiteDatabase}", never the
                 migration correctness suite's "{MigrationSuiteDatabase}", and never the
                 volume diagnostics' "{VolumeDiagnosticDatabase}".
                 Check DOWNTESTS_CONNECTION_STRING.
                 """);
        }
    }

    public static async Task DropIfExistsAsync()
    {
        AssertIsolated();
        SqlConnection.ClearAllPools();

        // SINGLE_USER WITH ROLLBACK IMMEDIATE, for the reason the other suites
        // give: a pooled connection left open by a previous run would make the
        // drop fail, and this run would then start rolling back a database whose
        // schema level it has not established.
        await ExecuteOnMasterAsync($"""
            IF DB_ID(N'{Name}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{Name}];
            END
            """);
    }

    public static Task CreateEmptyAsync()
    {
        AssertIsolated();
        return ExecuteOnMasterAsync($"IF DB_ID(N'{Name}') IS NULL CREATE DATABASE [{Name}];");
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

    /// <summary>Reads this database's schema. The queries are shared; the target is not.</summary>
    public static SqlSchemaReader Schema => new(ConnectionString);

    /// <summary>An open connection on the rollback database, for raw reads.</summary>
    public static async Task<SqlConnection> OpenAsync()
    {
        AssertIsolated();
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}
