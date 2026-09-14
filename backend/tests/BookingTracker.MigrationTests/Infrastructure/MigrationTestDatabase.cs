using Microsoft.Data.SqlClient;

namespace BookingTracker.MigrationTests.Infrastructure;

/// <summary>
/// The one place this suite's database is named, and the one place that refuses
/// to run against anything else.
///
/// Modelled on <c>SqlServerTestDatabase</c> (BookingTracker.SqlServerTests) and
/// on the browser suite's <c>assertIsolatedDatabase</c>, for the same reason all
/// three exist: the API's own appsettings.json points at the developer's
/// <c>BookingTracker</c>, so a dropped or misspelled override is the single
/// mistake here that would cost real data.
///
/// **This suite is more dangerous than the others and so the guard matters
/// more.** The other SQL-backed suites only ever add rows to a schema that is
/// already current. This one deliberately creates a database at an OLD
/// migration and then upgrades it - pointed at the developer's database it
/// would try to apply migrations that are already applied, and pointed at either
/// of the other test databases it would leave them at a schema level their own
/// fixtures do not expect.
///
/// Three names are therefore named and refused explicitly rather than merely
/// being "not ours": the development database, the browser suite's, and the SQL
/// Server integration suite's.
/// </summary>
public static class MigrationTestDatabase
{
    /// <summary>The development database this suite must never touch.</summary>
    public const string DevelopmentDatabase = "BookingTracker";

    /// <summary>The browser suite's database.</summary>
    public const string BrowserSuiteDatabase = "BookingTracker_E2E";

    /// <summary>The SQL Server integration suite's database.</summary>
    public const string SqlSuiteDatabase = "BookingTracker_SqlTests";

    public const string Name = "BookingTracker_MigrationTests";

    /// <summary>
    /// Overridable with MIGRATIONTESTS_CONNECTION_STRING for a differently-named
    /// local instance, but the database name itself is still checked - the
    /// override exists to point at another *server*, never at another database.
    /// </summary>
    public static string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("MIGRATIONTESTS_CONNECTION_STRING")
        ?? $"Server=localhost\\SQLEXPRESS;Database={Name};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    /// <summary>The same server, pointed at master, for CREATE/DROP DATABASE.</summary>
    public static string MasterConnectionString =>
        new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString;

    /// <summary>
    /// Refuses any connection string that does not name <see cref="Name"/>.
    /// Called before the first connection is opened, and again before the drop.
    /// </summary>
    public static void AssertIsolated()
    {
        var database = new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;

        if (!string.Equals(database, Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"""
                 The migration test suite refuses to run against database "{database}".
                 It must name "{Name}" - never the development database
                 "{DevelopmentDatabase}", never the browser suite's
                 "{BrowserSuiteDatabase}", and never the SQL Server integration
                 suite's "{SqlSuiteDatabase}". Check MIGRATIONTESTS_CONNECTION_STRING.
                 """);
        }
    }

    public static async Task DropIfExistsAsync()
    {
        AssertIsolated();
        SqlConnection.ClearAllPools();

        // SINGLE_USER WITH ROLLBACK IMMEDIATE, because a pooled connection left
        // open by a previous run would otherwise make the drop fail and the next
        // run start on a database that is already at the latest migration - which
        // would make this suite pass while proving nothing at all.
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
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>An open connection on the migration test database, for raw SQL.</summary>
    public static async Task<SqlConnection> OpenAsync()
    {
        AssertIsolated();
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}
