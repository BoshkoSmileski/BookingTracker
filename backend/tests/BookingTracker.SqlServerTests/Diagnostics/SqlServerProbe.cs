using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BookingTracker.SqlServerTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace BookingTracker.SqlServerTests.Diagnostics;

/// <summary>
/// Asks SQL Server itself what it did, rather than inferring it from elapsed
/// time.
///
/// Three questions, three mechanisms, all read-only and all pointed at the
/// throwaway <c>BookingTracker_SqlTests</c> database by
/// <see cref="SqlServerTestDatabase.ConnectionString"/>, which refuses to name
/// any other:
///
///  - <see cref="ActualPlanAsync"/> - SET STATISTICS XML ON, so the plan
///    reported is the one that RAN, with real row counts beside the estimates.
///    An estimated plan would not show a bad estimate turning into a scan.
///  - <see cref="LogicalReadsAsync"/> - SET STATISTICS IO ON. Elapsed time on a
///    warm cache is mostly noise; logical reads are the stable measure of how
///    much of the table the query touched.
///  - <see cref="RangeLocksAsync"/> - sys.dm_tran_locks read from inside an open
///    Serializable transaction. This is the only one that answers the question
///    this is actually about, since the deadlock is caused by the lock
///    footprint rather than by the read cost.
/// </summary>
public static class SqlServerProbe
{
    public sealed record PlanOperator(string Physical, string Logical, string? Index, double EstimateRows, double ActualRows);

    public sealed record PlanSummary(IReadOnlyList<PlanOperator> Operators, string Xml)
    {
        public override string ToString()
        {
            var text = new StringBuilder();
            foreach (var op in Operators)
            {
                text.AppendLine(
                    $"  {op.Physical,-28} {op.Logical,-16} {op.Index ?? "-",-52} " +
                    $"est={op.EstimateRows,10:F1} actual={op.ActualRows,8:F0}");
            }
            return text.ToString().TrimEnd();
        }
    }

    public sealed record LockSummary(string ResourceType, string Mode, string? Index, int Count);

    /// <summary>Runs <paramref name="sql"/> and returns the actual execution plan it used.</summary>
    public static async Task<PlanSummary> ActualPlanAsync(string sql)
    {
        await using var connection = await OpenAsync();

        await ExecuteAsync(connection, "SET STATISTICS XML ON");
        var xml = await ReadPlanXmlAsync(connection, sql);
        await ExecuteAsync(connection, "SET STATISTICS XML OFF");

        return new PlanSummary(ParseOperators(xml), xml);
    }

    /// <summary>Logical reads per table, as SQL Server reports them.</summary>
    public static async Task<IReadOnlyList<string>> LogicalReadsAsync(string sql)
    {
        await using var connection = await OpenAsync();

        var messages = new List<string>();
        connection.InfoMessage += (_, e) =>
        {
            foreach (var line in e.Message.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("Table ", StringComparison.Ordinal)) messages.Add(trimmed);
            }
        };

        await ExecuteAsync(connection, "SET STATISTICS IO ON");
        await DrainAsync(connection, sql);
        await ExecuteAsync(connection, "SET STATISTICS IO OFF");

        return messages;
    }

    /// <summary>
    /// The locks <paramref name="sql"/> holds once it has run under Serializable
    /// isolation, still inside the open transaction.
    ///
    /// RangeS-S is the one to read: a shared KEY-RANGE lock, which is what
    /// Serializable takes to make a repeated read see the same rows - and what
    /// two racing bookings then have to convert past each other to write. The
    /// transaction is rolled back, so nothing here changes a row.
    /// </summary>
    public static async Task<IReadOnlyList<LockSummary>> RangeLocksAsync(string sql)
    {
        await using var connection = await OpenAsync();

        // A real SqlTransaction rather than a "BEGIN TRANSACTION" batch: the
        // connection string enables MARS, and a transaction opened by one batch
        // and left open at the end of it is rolled back with
        // "A transaction that was started in a MARS batch is still active".
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable);

        try
        {
            await DrainAsync(connection, sql, transaction);

            // KEY/PAGE locks name an allocation unit, so they resolve through
            // sys.partitions.hobt_id; an OBJECT lock names the object itself.
            // Joining every row through hobt_id leaves the OBJECT lock unnamed -
            // which is exactly the row that matters here, because a table-level
            // S lock is what a full index scan takes under Serializable instead
            // of one range lock per row.
            const string lockQuery = """
                SELECT
                    l.resource_type,
                    l.request_mode,
                    ISNULL(
                        CASE l.resource_type
                            WHEN 'OBJECT' THEN OBJECT_NAME(l.resource_associated_entity_id)
                            ELSE OBJECT_NAME(p.object_id) + '.' + ISNULL(i.name, '(clustered)')
                        END, '') AS IndexName,
                    COUNT(*) AS Locks
                FROM sys.dm_tran_locks AS l
                LEFT JOIN sys.partitions AS p ON p.hobt_id = l.resource_associated_entity_id
                LEFT JOIN sys.indexes  AS i ON i.object_id = p.object_id AND i.index_id = p.index_id
                WHERE l.request_session_id = @@SPID
                  AND l.resource_type <> 'DATABASE'
                GROUP BY
                    l.resource_type, l.request_mode,
                    CASE l.resource_type
                        WHEN 'OBJECT' THEN OBJECT_NAME(l.resource_associated_entity_id)
                        ELSE OBJECT_NAME(p.object_id) + '.' + ISNULL(i.name, '(clustered)')
                    END
                ORDER BY COUNT(*) DESC;
                """;

            var results = new List<LockSummary>();
            await using var command = new SqlCommand(lockQuery, connection, transaction) { CommandTimeout = 120 };
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new LockSummary(
                    reader.GetString(0),
                    reader.GetString(1),
                    string.IsNullOrEmpty(reader.GetString(2)) ? null : reader.GetString(2),
                    reader.GetInt32(3)));
            }

            return results;
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    /// <summary>Wall-clock timing of the query, repeated so a cold first run does not become the answer.</summary>
    public static async Task<double[]> TimeAsync(string sql, int runs)
    {
        await using var connection = await OpenAsync();

        var timings = new double[runs];
        for (var i = 0; i < runs; i++)
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            await DrainAsync(connection, sql);
            timings[i] = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }
        return timings;
    }

    // ---- plumbing -----------------------------------------------------------

    private static async Task<SqlConnection> OpenAsync()
    {
        SqlServerTestDatabase.AssertIsolated();
        var connection = new SqlConnection(SqlServerTestDatabase.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Reads every result set to completion, so STATISTICS output is actually produced.</summary>
    private static async Task DrainAsync(SqlConnection connection, string sql, SqlTransaction? transaction = null)
    {
        await using var command = new SqlCommand(sql, connection, transaction) { CommandTimeout = 120 };
        await using var reader = await command.ExecuteReaderAsync();
        do
        {
            while (await reader.ReadAsync()) { }
        } while (await reader.NextResultAsync());
    }

    /// <summary>
    /// With STATISTICS XML on, the plan arrives as its own result set after the
    /// query's own rows - one row, one column, the whole plan document.
    /// </summary>
    private static async Task<string> ReadPlanXmlAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await using var reader = await command.ExecuteReaderAsync();

        string? plan = null;
        do
        {
            while (await reader.ReadAsync())
            {
                if (reader.FieldCount == 1 && reader.GetValue(0) is string value &&
                    value.Contains("ShowPlanXML", StringComparison.Ordinal))
                {
                    plan = value;
                }
            }
        } while (await reader.NextResultAsync());

        return plan ?? throw new InvalidOperationException("SQL Server returned no execution plan.");
    }

    private static IReadOnlyList<PlanOperator> ParseOperators(string xml)
    {
        var document = XDocument.Parse(xml);
        XNamespace ns = document.Root!.GetDefaultNamespace();

        var operators = new List<PlanOperator>();
        foreach (var relOp in document.Descendants(ns + "RelOp"))
        {
            var index = relOp.Descendants(ns + "Object").FirstOrDefault();
            var indexName = index is null
                ? null
                : $"{Unbracket(index.Attribute("Table")?.Value)}.{Unbracket(index.Attribute("Index")?.Value) ?? "(heap)"}";

            var actual = relOp.Descendants(ns + "RunTimeCountersPerThread")
                .Sum(t => double.TryParse(t.Attribute("ActualRows")?.Value, out var rows) ? rows : 0);

            operators.Add(new PlanOperator(
                relOp.Attribute("PhysicalOp")?.Value ?? "?",
                relOp.Attribute("LogicalOp")?.Value ?? "?",
                indexName,
                double.TryParse(relOp.Attribute("EstimateRows")?.Value, out var estimate) ? estimate : 0,
                actual));
        }

        return operators;
    }

    private static string? Unbracket(string? value)
        => value is null ? null : Regex.Replace(value, @"^\[|\]$", string.Empty);
}
