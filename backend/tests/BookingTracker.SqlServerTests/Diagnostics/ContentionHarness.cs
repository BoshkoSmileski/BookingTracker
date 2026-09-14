using System.Diagnostics;
using System.Net;
using BookingTracker.IntegrationTests.Infrastructure;
using BookingTracker.SqlServerTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace BookingTracker.SqlServerTests.Diagnostics;

/// <summary>
/// Runs N booking submissions genuinely at once and reports what came back.
///
/// This is the 2-racer gate from <c>SlotConflictConcurrencyTests</c> widened to
/// any number of racers, and given a second scenario that turns out to matter
/// more than the first. Same mechanism, deliberately: every racer is prepared to
/// the point of submitting, held on one TaskCompletionSource, and released
/// together, with no Task.Delay anywhere - a manufactured pause would prove
/// nothing about timing.
///
/// Deadlocks are counted from SQL Server's own cumulative counter rather than
/// inferred from status codes. A lost race is answered with 409 exactly like an
/// ordinary conflict (that is the whole point of the lost-race translation), so from
/// the outside the two are indistinguishable - and "how often does this deadlock"
/// is precisely the question this has to answer.
/// </summary>
public static class ContentionHarness
{
    public sealed record Attempt(HttpStatusCode Status, string Body, Guid SessionId, double ElapsedMs);

    public sealed record Result(
        IReadOnlyList<Attempt> Attempts,
        int Deadlocks,
        double WallClockMs)
    {
        public int Ok => Attempts.Count(a => a.Status == HttpStatusCode.OK);
        public int Conflicts => Attempts.Count(a => a.Status == HttpStatusCode.Conflict);
        public int ServerErrors => Attempts.Count(a => (int)a.Status >= 500);
        public int Other => Attempts.Count - Ok - Conflicts - ServerErrors;

        /// <summary>
        /// Anything that is neither the booking nor the conflict answer, with the
        /// body kept. A count alone would say a race went wrong without saying
        /// how, and "how" is the entire finding at higher contention.
        /// </summary>
        public IEnumerable<string> Unexpected => Attempts
            .Where(a => a.Status is not (HttpStatusCode.OK or HttpStatusCode.Conflict))
            .Select(a => $"{(int)a.Status} {a.Status}: {Trim(a.Body)}")
            .Distinct();

        private static string Trim(string body)
            => body.Length <= 200 ? body : body[..200] + "...";

        public double P50 => Percentile(50);
        public double P95 => Percentile(95);
        public double Max => Attempts.Count == 0 ? 0 : Attempts.Max(a => a.ElapsedMs);

        private double Percentile(int percentile)
        {
            if (Attempts.Count == 0) return 0;
            var ordered = Attempts.Select(a => a.ElapsedMs).OrderBy(ms => ms).ToArray();
            var index = (int)Math.Ceiling(percentile / 100.0 * ordered.Length) - 1;
            return ordered[Math.Clamp(index, 0, ordered.Length - 1)];
        }
    }

    /// <summary>One prepared, about-to-submit booking session and the client that owns it.</summary>
    public sealed record Racer(HttpClient Client, Guid SessionId);

    public static async Task<Result> RaceAsync(IReadOnlyList<Racer> racers)
    {
        var deadlocksBefore = await DeadlockCountAsync();

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = racers.Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();

        var tasks = racers
            .Select((racer, i) => SubmitWhenReleasedAsync(racer, ready[i], gate.Task))
            .ToArray();

        await Task.WhenAll(ready.Select(r => r.Task));

        var started = Stopwatch.GetTimestamp();
        gate.SetResult();
        var attempts = await Task.WhenAll(tasks);
        var wallClock = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        var deadlocksAfter = await DeadlockCountAsync();

        return new Result(attempts, (int)(deadlocksAfter - deadlocksBefore), wallClock);
    }

    private static async Task<Attempt> SubmitWhenReleasedAsync(Racer racer, TaskCompletionSource ready, Task gate)
    {
        await Task.Yield();
        ready.SetResult();
        await gate;

        var started = Stopwatch.GetTimestamp();
        var response = await BookingFlow.SubmitAsync(racer.Client, racer.SessionId);
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var body = await response.Content.ReadAsStringAsync();

        return new Attempt(response.StatusCode, body, racer.SessionId, elapsed);
    }

    /// <summary>
    /// SQL Server's cumulative deadlock count. "Number of Deadlocks/sec" is the
    /// counter's name, not its semantics - the value is a running total since the
    /// instance started, so the difference across a race is how many deadlocks
    /// that race caused.
    ///
    /// Instance-wide, so nothing else may be hitting this server during a
    /// measurement. That is already true here: the collection runs with
    /// parallelisation off and the database is exclusive to the run.
    /// </summary>
    public static async Task<long> DeadlockCountAsync()
    {
        SqlServerTestDatabase.AssertIsolated();

        await using var connection = new SqlConnection(SqlServerTestDatabase.ConnectionString);
        await connection.OpenAsync();

        await using var command = new SqlCommand("""
            SELECT ISNULL(SUM(cntr_value), 0)
            FROM sys.dm_os_performance_counters
            WHERE counter_name = 'Number of Deadlocks/sec' AND instance_name = '_Total';
            """, connection);

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
