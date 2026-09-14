using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace BookingTracker.MigrationTests.Diagnostics;

/// <summary>
/// An application-shaped read, repeated on its own connection while a migration
/// runs, so "did this migration lock anything that matters" is answered by
/// something actually trying to use the table rather than by reading a lock list
/// and reasoning about it.
///
/// <b>Two queries, and each is the real one in shape.</b>
///
///  - <see cref="ConflictShapedRead"/> is <c>BookingConflictChecker</c>'s
///    question: which of this organizer's bookings are on this date. It reaches
///    BookingSessions by joining through BookingPages because BookingSessions has
///    no OrganizerId column - the same reason the conflict index gives - and it
///    is the read <c>NarrowBookingConflictLockFootprint</c>'s index exists to
///    serve. Every public booking submission in the product runs it.
///  - <see cref="AvailabilityShapedRead"/> is the availability window load:
///    this organizer's exceptions overlapping a date range. It is what
///    <c>AddAvailabilityExceptionEndDate</c>'s table backs.
///
/// <b>Both are written to be valid at EVERY schema level in the tested range</b>,
/// which is a real constraint rather than a stylistic one: the reader has to
/// survive the migration that is running underneath it. So the availability read
/// names <c>Date</c> and never <c>EndDate</c> (which does not exist until the
/// migration under measurement adds it), and the conflict read names only columns
/// that are present throughout.
///
/// <b>Plain SQL rather than the application's own handler</b>, deliberately. The
/// application cannot run against a half-migrated database at all - its model
/// expects the final schema - so driving it would measure a startup failure. And
/// this must add no production hook to make a benchmark possible. What is
/// reused is the query's SHAPE, which is the part that determines what it locks.
/// </summary>
public sealed class ConcurrentReader : IAsyncDisposable
{
    /// <summary>Which of this organizer's bookings are on this date - the claim path's question.</summary>
    public const string ConflictShapedRead = """
        SELECT COUNT_BIG(*)
        FROM [BookingSessions] AS s
        INNER JOIN [BookingPages] AS p ON p.[Id] = s.[BookingPageId]
        WHERE p.[OrganizerId] = @organizerId
          AND s.[Status] = 1
          AND s.[SelectedDate] = @date;
        """;

    /// <summary>This organizer's blocked periods around a date - valid before and after EndDate exists.</summary>
    public const string AvailabilityShapedRead = """
        SELECT COUNT_BIG(*)
        FROM [AvailabilityExceptions] AS e
        WHERE e.[OrganizerId] = @organizerId
          AND e.[Date] >= @date;
        """;

    public sealed record Result(int Iterations, double MedianMs, double MaxMs, int Failures)
    {
        public override string ToString()
            => $"{Iterations} reads, median {MedianMs:F1} ms, max {MaxMs:F1} ms" +
               (Failures > 0 ? $", {Failures} failed" : string.Empty);
    }

    private readonly SqlConnection _connection;
    private readonly string _sql;
    private readonly Guid _organizerId;
    private readonly DateOnly _date;
    private readonly List<double> _latencies = [];
    private readonly CancellationTokenSource _stop = new();

    private int _failures;
    private Task? _loop;

    private ConcurrentReader(SqlConnection connection, string sql, Guid organizerId, DateOnly date)
    {
        _connection = connection;
        _sql = sql;
        _organizerId = organizerId;
        _date = date;
    }

    public short SessionId { get; private set; }

    public static async Task<ConcurrentReader> OpenAsync(string sql, Guid organizerId, DateOnly date)
    {
        var connection = await VolumeDiagnosticDatabase.OpenAsync();
        var reader = new ConcurrentReader(connection, sql, organizerId, date);

        await using (var command = new SqlCommand("SELECT @@SPID;", connection))
        {
            reader.SessionId = (short)(await command.ExecuteScalarAsync())!;
        }

        return reader;
    }

    /// <summary>
    /// One read, timed, before anything else is running. This is the baseline the
    /// during-migration numbers are compared against - without it, a 40 ms read
    /// during a migration is a number with nothing to mean.
    /// </summary>
    public async Task<double> WarmAndTimeOnceAsync()
    {
        await RunOnceAsync(CancellationToken.None);           // warm the cache and the plan
        var started = Stopwatch.GetTimestamp();
        await RunOnceAsync(CancellationToken.None);
        return Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    public void Start() => _loop = Task.Run(() => LoopAsync(_stop.Token));

    public async Task<Result> StopAsync()
    {
        await _stop.CancelAsync();
        if (_loop is not null)
        {
            try { await _loop; } catch (OperationCanceledException) { /* expected */ }
        }

        if (_latencies.Count == 0) return new Result(0, 0, 0, _failures);

        var sorted = _latencies.Order().ToArray();
        return new Result(sorted.Length, sorted[sorted.Length / 2], sorted[^1], _failures);
    }

    private async Task LoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                // Deliberately NOT cancelled mid-flight: a read that is blocked by
                // the migration must be allowed to finish so its true latency is
                // recorded. Cancelling it would report the blocking window as
                // shorter than it is, which is the one direction this measurement
                // must never be wrong in.
                await RunOnceAsync(CancellationToken.None);
                _latencies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            catch (SqlException)
            {
                // A schema change under a running read can legitimately fail it.
                // That is itself a finding, so it is counted rather than retried
                // silently or allowed to fail the diagnostic.
                _failures++;
            }

            try { await Task.Delay(TimeSpan.FromMilliseconds(25), token); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task RunOnceAsync(CancellationToken token)
    {
        await using var command = new SqlCommand(_sql, _connection) { CommandTimeout = 600 };
        command.Parameters.AddWithValue("@organizerId", _organizerId);
        command.Parameters.AddWithValue("@date", _date.ToDateTime(TimeOnly.MinValue));
        await command.ExecuteScalarAsync(token);
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Dispose();
        await _connection.DisposeAsync();
    }
}
