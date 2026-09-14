using System.Diagnostics;
using BookingTracker.SqlServerTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Xunit.Abstractions;

namespace BookingTracker.SqlServerTests.Diagnostics;

/// <summary>
/// what the slot claim's read locks, and what two of them do to each
/// other.
///
/// The covering index made the conflict query a single seek and confined its
/// range lock to one booking page, which fixed guests who were never in
/// competition. It did nothing for guests who genuinely are, because the cost
/// there was never the read - it was that two racers hold the SAME key
/// <c>RangeS-S</c> and then both try to convert it to <c>RangeI-N</c> to record
/// their own booking. Neither can be granted while the other holds the shared
/// half, so the answer arrives via the deadlock monitor rather than via a lock
/// wait.
///
/// Reports rather than asserts - see <see cref="DiagnosticFactAttribute"/>. The
/// contract these numbers justify is asserted in
/// <see cref="SlotConflictConcurrencyTests"/>, which runs in CI.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class ClaimLockDiagnostics(ITestOutputHelper output)
{
    [DiagnosticFact]
    public async Task WhatTheClaimLocks()
    {
        var shape = await ConflictDataset.EnsureSeededAsync(output.WriteLine);

        await ConflictQuery.AssertGateMatchesProductionAsync(
            shape.TargetPageId, shape.TargetDate, shape.FreeTime, output.WriteLine);

        output.WriteLine($"""

            The gate statement, as production issues it
            -------------------------------------------
            {ConflictQuery.GateSql(shape.TargetPageId)}

            Plan
            ----
            {await SqlServerProbe.ActualPlanAsync(ConflictQuery.GateSql(shape.TargetPageId))}
            """);

        await ReportAsync(
            "GATE without the claim lock - shared, so both racers pass",
            ConflictQuery.GateSql(shape.TargetPageId, withClaimLock: false));

        await ReportAsync(
            "GATE with the claim lock - update, so the second racer waits here",
            ConflictQuery.GateSql(shape.TargetPageId));

        await ReportAsync(
            "The conflict read that follows it, unchanged by the claim gate",
            ConflictQuery.Sql(shape.TargetOrganizerId, Guid.NewGuid(), shape.TargetDate));
    }

    /// <summary>
    /// The protocol itself, with two real connections rather than by inference:
    /// A reads and holds, B reads and must BLOCK ON A, A commits, B proceeds.
    ///
    /// What makes this evidence rather than a timing coincidence is that B's
    /// wait is read out of <c>sys.dm_exec_requests</c> while it is happening -
    /// the wait type, the resource, and the session it is blocked by are all
    /// reported. The same sequence without the hint is run first, where B does
    /// not block at all, which is exactly the problem.
    /// </summary>
    [DiagnosticFact]
    public async Task TwoClaimReadsTakeTurnsInsteadOfColliding()
    {
        var shape = await ConflictDataset.EnsureSeededAsync(output.WriteLine);

        await ProbeAsync(
            "WITHOUT the claim lock",
            ConflictQuery.GateSql(shape.TargetPageId, withClaimLock: false));

        await ProbeAsync(
            "WITH the claim lock",
            ConflictQuery.GateSql(shape.TargetPageId));
    }

    // ---- plumbing -----------------------------------------------------------

    private async Task ProbeAsync(string title, string sql)
    {
        await using var connectionA = await OpenAsync();
        await using var connectionB = await OpenAsync();

        var spidB = await ScalarAsync<short>(connectionB, "SELECT @@SPID");

        await using var transactionA = (SqlTransaction)await connectionA.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);
        await using var transactionB = (SqlTransaction)await connectionB.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);

        await DrainAsync(connectionA, sql, transactionA);

        var readB = DrainAsync(connectionB, sql, transactionB);
        var blocked = await WaitForBlockAsync(spidB);

        output.WriteLine($"""

            {title}
            {new string('-', title.Length)}
              B blocked by A : {(blocked is null ? "no - both reads proceeded together" : "yes")}
              {blocked ?? "(so both will need to convert their shared lock, and neither can)"}
            """);

        await transactionA.RollbackAsync();

        var completed = await Task.WhenAny(readB, Task.Delay(TimeSpan.FromSeconds(15))) == readB;
        await transactionB.RollbackAsync();

        output.WriteLine($"  B completed after A released: {(completed ? "yes" : "NO - still waiting after 15s")}");
        if (completed) await readB;
    }

    private async Task ReportAsync(string title, string sql)
    {
        output.WriteLine($"""

            {title}
            {new string('-', title.Length)}
            """);

        foreach (var held in await SqlServerProbe.RangeLocksAsync(sql))
        {
            output.WriteLine($"  {held.ResourceType,-8} {held.Mode,-12} {held.Index ?? "-",-58} x{held.Count}");
        }
    }

    /// <summary>
    /// Polls sys.dm_exec_requests until the session is waiting on a lock, and
    /// reports what it is waiting for. Returns null if it never waits - which is
    /// a real answer here, not a timeout to work around, so the budget is short.
    /// </summary>
    private static async Task<string?> WaitForBlockAsync(short spid)
    {
        await using var observer = await OpenAsync();
        var deadline = Stopwatch.GetTimestamp() + (long)(3 * Stopwatch.Frequency);

        while (Stopwatch.GetTimestamp() < deadline)
        {
            await using var command = new SqlCommand("""
                SELECT r.wait_type, r.blocking_session_id, r.wait_resource
                FROM sys.dm_exec_requests AS r
                WHERE r.session_id = @spid AND r.wait_type LIKE 'LCK[_]%';
                """, observer);
            command.Parameters.AddWithValue("@spid", spid);

            await using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return $"wait_type={reader.GetString(0)}  blocked_by_spid={reader.GetInt16(1)}  " +
                       $"resource={reader.GetString(2)}";
            }
        }

        return null;
    }

    private static async Task<SqlConnection> OpenAsync()
    {
        SqlServerTestDatabase.AssertIsolated();
        var connection = new SqlConnection(SqlServerTestDatabase.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<T> ScalarAsync<T>(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task DrainAsync(SqlConnection connection, string sql, SqlTransaction transaction)
    {
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 120 };
        await using var reader = await command.ExecuteReaderAsync();
        do
        {
            while (await reader.ReadAsync()) { }
        } while (await reader.NextResultAsync());
    }
}
