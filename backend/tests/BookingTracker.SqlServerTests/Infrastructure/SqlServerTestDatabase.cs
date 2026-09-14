using Microsoft.Data.SqlClient;

namespace BookingTracker.SqlServerTests.Infrastructure;

/// <summary>
/// The one place this suite's database is named, and the one place that refuses
/// to run against anything else.
///
/// Modelled on the browser suite's <c>assertIsolatedDatabase</c> (e2e/support/env.mjs)
/// for the same reason: the API's own appsettings.json points at the developer's
/// <c>BookingTracker</c>, so a dropped or misspelled override is the single
/// mistake here that would cost real data.
///
/// **Why not reuse <c>BookingTracker_E2E</c>.** It is dropped and recreated by
/// <c>npm run test:e2e:reset</c>, and the Playwright global setup asserts things
/// about its contents (that the seeded demo organizer cannot sign in). Sharing
/// it would make each suite able to break the other's preconditions, and would
/// make <c>dotnet test</c> and <c>npm run test:e2e</c> unsafe to run at the same
/// time. A separate throwaway database costs nothing and keeps the two
/// independent - which the phase brief explicitly asks for.
/// </summary>
public static class SqlServerTestDatabase
{
    /// <summary>The development database this suite must never touch.</summary>
    public const string DevelopmentDatabase = "BookingTracker";

    /// <summary>The browser suite's database. Also off limits - see the class remarks.</summary>
    public const string BrowserSuiteDatabase = "BookingTracker_E2E";

    public const string Name = "BookingTracker_SqlTests";

    /// <summary>
    /// Overridable with SQLTESTS_CONNECTION_STRING for a differently-named local
    /// instance, but the database name itself is still checked - the override
    /// exists to point at another *server*, never at another database.
    /// </summary>
    public static string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("SQLTESTS_CONNECTION_STRING")
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
                 The SQL Server test suite refuses to run against database "{database}".
                 It must name "{Name}" - never the development database
                 "{DevelopmentDatabase}", and never the browser suite's
                 "{BrowserSuiteDatabase}". Check SQLTESTS_CONNECTION_STRING.
                 """);
        }
    }

    public static async Task DropIfExistsAsync()
    {
        AssertIsolated();
        SqlConnection.ClearAllPools();

        // SINGLE_USER WITH ROLLBACK IMMEDIATE, because a pooled connection left
        // open by a previous run would otherwise make the drop fail and the next
        // run start on stale rows.
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
}
