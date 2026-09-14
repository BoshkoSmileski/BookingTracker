using System.Collections.Concurrent;
using Microsoft.Data.SqlClient;

namespace BookingTracker.MigrationTests.Diagnostics;

/// <summary>
/// Watches a migration WHILE IT RUNS, from a second connection, and reports what
/// SQL Server itself says it did.
///
/// <b>Why this is not <c>SqlServerProbe</c>.</b> That class (BookingTracker.SqlServerTests)
/// answers "what does a query I hand it hold?" - it runs the statement itself,
/// inside its own explicit transaction, and reads
/// <c>sys.dm_tran_locks WHERE request_session_id = @@SPID</c>. Every one of those
/// is wrong for this question. Here the statement is a migration EF is executing
/// on another connection, the transaction is EF's, and the whole point is to
/// observe a session that is NOT this one, from outside, while it is in flight.
/// It is also pinned to <c>SqlServerTestDatabase.ConnectionString</c>, which
/// refuses to name the diagnostic database. Same DMVs, opposite direction - so
/// the mechanism is reused and the class could not be.
///
/// Three independent measurements, because they answer three different halves of
/// "is this a deployment-risk window":
///
///  - <b>Session counters</b> (<c>sys.dm_exec_sessions</c>, sampled before and
///    after) - CPU, logical reads, physical reads and writes actually attributed
///    to the migration. Elapsed time on its own cannot distinguish a migration
///    that read the whole table from one that changed metadata and returned.
///  - <b>Lock and wait sampling</b> (<c>sys.dm_exec_requests</c> +
///    <c>sys.dm_tran_locks</c>, polled) - the peak lock footprint by resource
///    type and mode, and every wait type seen. <c>Sch-M</c> on an OBJECT is the
///    one that decides the answer: a schema-modification lock is incompatible
///    with EVERYTHING, including readers, so for as long as it is held the table
///    is unavailable to the application.
///  - <b>Transaction log bytes</b> (<c>sys.dm_tran_database_transactions</c>) -
///    how much log one migration's transaction accumulates before it commits,
///    which is both a disk-space question and a how-long-would-a-rollback-take
///    question.
///
/// Read-only throughout: nothing here writes a row, and the connection is opened
/// through <see cref="VolumeDiagnosticDatabase"/>, which refuses to name any
/// database but the diagnostic one.
/// </summary>
public sealed class MigrationObserver : IAsyncDisposable
{
    // The sampler runs with NO artificial delay between samples, and that is a
    // measured decision rather than laziness.
    //
    // The smallest thing worth catching is a schema-modification lock on a
    // migration that finishes in a few tens of milliseconds, because a sampler
    // that misses one reports "no locks observed" for a migration that did in
    // fact take the table away from the application - the wrong direction for
    // this measurement to be wrong in. A Task.Delay of 5ms does not give 5ms:
    // Windows' default timer resolution is ~15.6ms, so the loop measured at ~25ms
    // per sample and a 40ms migration was observed once or twice, intermittently.
    //
    // Each iteration already performs a real network round trip, which throttles
    // the loop to roughly 5-10ms on its own - so removing the delay costs one
    // connection running four trivial DMV reads, and buys reliable observation of
    // the short locks. The sample count is reported per migration so the
    // resolution of any given measurement is visible rather than assumed.

    public sealed record LockPeak(string ResourceType, string Mode, string? Object, int Peak)
    {
        public override string ToString() => $"{ResourceType} {Mode} on {Object ?? "-"} x{Peak}";
    }

    public sealed record SessionCounters(long CpuMs, long LogicalReads, long PhysicalReads, long Writes)
    {
        public static SessionCounters operator -(SessionCounters a, SessionCounters b)
            => new(a.CpuMs - b.CpuMs, a.LogicalReads - b.LogicalReads, a.PhysicalReads - b.PhysicalReads, a.Writes - b.Writes);
    }

    public sealed record Observation(
        IReadOnlyList<LockPeak> Locks,
        IReadOnlyCollection<string> WaitTypes,
        bool SchemaModificationLock,
        long PeakLogBytes,
        int Samples,
        bool ReaderBlockedByMigration,
        int MaxReaderWaitMs,
        IReadOnlyCollection<string> ReaderWaitTypes)
    {
        /// <summary>The peak of the one lock class that makes a table unavailable to readers.</summary>
        public IReadOnlyList<LockPeak> BlockingLocks =>
            Locks.Where(l => l.Mode is "Sch-M" or "X" && l.ResourceType == "OBJECT").ToArray();
    }

    private readonly SqlConnection _connection;

    /// <summary>
    /// Peaks keyed by RAW entity id, resolved to object names only after the
    /// migration has finished - see <see cref="ResolveNamesAsync"/> for why that
    /// separation is not a style choice.
    /// </summary>
    private readonly ConcurrentDictionary<(string Type, string Mode, long Entity), int> _peaks = new();
    private readonly ConcurrentDictionary<string, byte> _waits = new();
    private readonly ConcurrentDictionary<string, byte> _readerWaits = new();
    private readonly CancellationTokenSource _stop = new();

    private long _peakLogBytes;
    private int _samples;
    private int _maxReaderWaitMs;
    private bool _readerBlocked;
    private Task? _polling;

    private MigrationObserver(SqlConnection connection) => _connection = connection;

    public static async Task<MigrationObserver> OpenAsync()
        => new(await VolumeDiagnosticDatabase.OpenAsync());

    /// <summary>Cumulative counters for <paramref name="sessionId"/>, for a before/after delta.</summary>
    public async Task<SessionCounters> ReadCountersAsync(short sessionId)
    {
        const string sql = """
            SELECT cpu_time, logical_reads, reads, writes
            FROM sys.dm_exec_sessions
            WHERE session_id = @spid;
            """;

        await using var command = new SqlCommand(sql, _connection) { CommandTimeout = 60 };
        command.Parameters.AddWithValue("@spid", sessionId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException(
                $"Session {sessionId} is not open. The migration connection must be opened explicitly " +
                "and kept open, or its counters reset between statements and the delta means nothing.");
        }

        return new SessionCounters(reader.GetInt32(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
    }

    /// <summary>
    /// Starts sampling until <see cref="StopAsync"/>.
    ///
    /// <paramref name="readerSessionId"/> is the concurrent application-shaped
    /// reader, when one is running. Watching it is what turns "the migration held
    /// an X lock" into the answer the deployment question actually needs -
    /// whether an ordinary read WAITED, and specifically whether it waited on
    /// THIS migration rather than on something incidental.
    /// </summary>
    /// <remarks>
    /// Awaited until the loop has genuinely entered its first iteration. Without
    /// that, <c>Task.Run</c> can still be queued when a 30ms migration starts and
    /// finishes, and the migration would be reported as holding no locks because
    /// nothing was watching yet rather than because it held none.
    /// </remarks>
    public async Task StartAsync(short sessionId, short? readerSessionId = null)
    {
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _polling = Task.Run(() => PollAsync(sessionId, readerSessionId, running, _stop.Token));
        await running.Task;
    }

    public async Task<Observation> StopAsync()
    {
        await _stop.CancelAsync();
        if (_polling is not null)
        {
            try { await _polling; } catch (OperationCanceledException) { /* expected */ }
        }

        var names = await ResolveNamesAsync();
        var locks = _peaks
            .Select(kv => new LockPeak(
                kv.Key.Type, kv.Key.Mode, names.GetValueOrDefault((kv.Key.Type, kv.Key.Entity)), kv.Value))
            .OrderByDescending(l => l.Peak)
            .ToArray();

        return new Observation(
            locks,
            _waits.Keys.OrderBy(w => w).ToArray(),
            SchemaModificationLock: locks.Any(l => l.Mode == "Sch-M"),
            PeakLogBytes: Interlocked.Read(ref _peakLogBytes),
            Samples: _samples,
            ReaderBlockedByMigration: _readerBlocked,
            MaxReaderWaitMs: _maxReaderWaitMs,
            ReaderWaitTypes: _readerWaits.Keys.OrderBy(w => w).ToArray());
    }

    private async Task PollAsync(
        short sessionId, short? readerSessionId, TaskCompletionSource running, CancellationToken token)
    {
        // ONE round trip per sample, four result sets - not four commands.
        //
        // Four separate reads cost four network round trips plus four command
        // set-ups, which measured at roughly 20ms per sample against a 5ms
        // interval: a migration that finished in 40ms was observed twice. Batched,
        // a sample costs one round trip, so the interval is the interval.
        //
        // And deliberately NO OBJECT_NAME anywhere in it. The obvious version of
        // the lock query - the one SqlServerProbe uses, joining sys.partitions and
        // resolving names inline - was written first and was measurably wrong
        // here: resolving a name touches catalog metadata, which a migration
        // holding schema locks is in the middle of changing, so THE OBSERVER
        // BLOCKED ON THE VERY LOCK IT WAS TRYING TO OBSERVE. Three samples in
        // 414ms, and a concurrent read that waited 351ms reported as never
        // blocked, because nothing was looking during the window. Raw ids here;
        // names resolved once, after the migration commits.
        const string sampleSql = """
            SELECT l.resource_type, l.request_mode, l.resource_associated_entity_id, COUNT(*)
            FROM sys.dm_tran_locks AS l
            WHERE l.request_session_id = @spid AND l.resource_type <> 'DATABASE'
            GROUP BY l.resource_type, l.request_mode, l.resource_associated_entity_id;

            SELECT ISNULL(r.wait_type, ''), ISNULL(r.blocking_session_id, 0)
            FROM sys.dm_exec_requests AS r
            WHERE r.session_id = @spid;

            SELECT ISNULL(MAX(dt.database_transaction_log_bytes_used), 0)
            FROM sys.dm_tran_database_transactions AS dt
            JOIN sys.dm_tran_session_transactions AS st ON st.transaction_id = dt.transaction_id
            WHERE st.session_id = @spid AND dt.database_id = DB_ID();

            SELECT ISNULL(r.wait_type, ''), ISNULL(r.blocking_session_id, 0),
                   ISNULL(r.wait_time, 0), ISNULL(r.wait_resource, '')
            FROM sys.dm_exec_requests AS r
            WHERE r.session_id = @reader;
            """;

        running.TrySetResult();

        while (!token.IsCancellationRequested)
        {
            // Counted before the read rather than after, so a sample that threw is
            // still a sample that happened - otherwise a run where every DMV read
            // failed would report "0 samples", which reads identically to "the
            // observer never started".
            _samples++;

            try
            {
                await SampleAsync(sampleSql, sessionId, readerSessionId ?? -1, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SqlException)
            {
                // The observer must never be able to fail the measurement it is
                // watching. A transient read of a DMV that is mid-update is not
                // information; the next sample is.
            }
        }
    }

    private async Task SampleAsync(string sql, short sessionId, short readerSessionId, CancellationToken token)
    {
        await using var command = new SqlCommand(sql, _connection) { CommandTimeout = 30 };
        command.Parameters.AddWithValue("@spid", sessionId);
        command.Parameters.AddWithValue("@reader", readerSessionId);

        await using var reader = await command.ExecuteReaderAsync(token);

        // 1 - the migration's lock footprint, peak per (type, mode, entity).
        while (await reader.ReadAsync(token))
        {
            var key = (reader.GetString(0), reader.GetString(1), reader.GetInt64(2));
            var count = reader.GetInt32(3);
            _peaks.AddOrUpdate(key, count, (_, existing) => Math.Max(existing, count));
        }

        // 2 - what the migration itself is waiting on. A migration that is BLOCKED
        //     is worth knowing about: the deployment window is then longer than the
        //     migration's own work.
        await reader.NextResultAsync(token);
        while (await reader.ReadAsync(token))
        {
            var wait = reader.GetString(0);
            var blocker = reader.GetInt16(1);
            if (!string.IsNullOrEmpty(wait)) _waits.TryAdd(wait, 0);
            if (blocker != 0) _waits.TryAdd($"BLOCKED BY spid {blocker}", 0);
        }

        // 3 - transaction log accumulated so far by the migration's transaction.
        await reader.NextResultAsync(token);
        while (await reader.ReadAsync(token))
        {
            _peakLogBytes = Math.Max(_peakLogBytes, reader.GetInt64(0));
        }

        // 4 - the concurrent application read. blocking_session_id equal to the
        //     migration's spid is SQL Server stating outright that an ordinary
        //     read is waiting on this migration - which is the whole deployment
        //     question, answered by the server rather than inferred from latency.
        await reader.NextResultAsync(token);
        while (await reader.ReadAsync(token))
        {
            if (reader.GetInt16(1) != sessionId) continue;

            _readerBlocked = true;
            _maxReaderWaitMs = Math.Max(_maxReaderWaitMs, reader.GetInt32(2));

            var wait = reader.GetString(0);
            var resource = reader.GetString(3);
            _readerWaits.TryAdd(
                $"{(string.IsNullOrEmpty(wait) ? "(none)" : wait)} on {(string.IsNullOrEmpty(resource) ? "(unnamed)" : resource)}",
                0);
        }
    }

    /// <summary>
    /// Turns the raw entity ids the sampler recorded into object names, once,
    /// after the migration has committed and the catalog is readable again.
    ///
    /// An OBJECT lock's entity is an <c>object_id</c>; a KEY/PAGE/RID lock's is a
    /// <c>hobt_id</c>. Anything else (METADATA, APPLICATION, an allocation unit)
    /// has no useful name and is reported without one rather than guessed at.
    /// </summary>
    private async Task<Dictionary<(string, long), string>> ResolveNamesAsync()
    {
        var names = new Dictionary<(string, long), string>();
        var wanted = _peaks.Keys.Select(k => (k.Type, k.Entity)).Distinct().ToArray();
        if (wanted.Length == 0) return names;

        const string sql = """
            SELECT 'OBJECT', CAST(o.object_id AS bigint), o.name FROM sys.objects AS o
            UNION ALL
            SELECT l.t, p.hobt_id, OBJECT_NAME(p.object_id)
            FROM sys.partitions AS p
            CROSS JOIN (VALUES ('KEY'), ('PAGE'), ('RID'), ('HOBT')) AS l(t);
            """;

        await using var command = new SqlCommand(sql, _connection) { CommandTimeout = 60 };
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (reader.IsDBNull(2)) continue;
            var key = (reader.GetString(0), reader.GetInt64(1));
            if (wanted.Contains(key)) names[key] = reader.GetString(2);
        }

        return names;
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Dispose();
        await _connection.DisposeAsync();
    }
}
