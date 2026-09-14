using BookingTracker.IntegrationTests.Infrastructure;
using BookingTracker.SqlServerTests.Infrastructure;
using Xunit.Abstractions;

namespace BookingTracker.SqlServerTests.Diagnostics;

/// <summary>
/// Investigation step 4: what contention actually costs, at four
/// levels, in two scenarios that must not be confused with each other.
///
///  - **Contested** - every racer wants the SAME slot. Exactly one booking is
///    the correct outcome, so N-1 rejections are the product working. What is
///    being measured here is the PRICE of that answer, not whether it is right.
///  - **Independent** - every racer books a DIFFERENT organizer's page, so no
///    two of them conflict in any way the domain recognises. Every one of them
///    should succeed. A rejection or an error here is contention invented by the
///    storage engine, and it is the number this phase exists to find.
///
/// Correctness is asserted in both, at every level, because a benchmark that
/// stops checking what it measures is how a "faster" result gets accepted for
/// the wrong reason. What is NOT asserted is any wall-clock threshold: the
/// numbers are reported for comparison between runs, and a machine having a busy
/// moment must never fail a build.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class ContentionBenchmark(ITestOutputHelper output) : SqlServerApiTestBase
{
    private static readonly int[] Levels = [2, 4, 8, 16];
    private const int Rounds = 3;

    /// <summary>
    /// Set to <c>A</c> or <c>B</c> to apply a candidate index shape to the
    /// throwaway database before measuring, so a before/after comparison is one
    /// environment variable rather than two edited files.
    ///
    /// Only ever DDL on <c>BookingTracker_SqlTests</c> - see
    /// <see cref="IndexCandidates"/>. Once a candidate is chosen it becomes an EF
    /// migration and this switch stops being how it is applied.
    /// </summary>
    private const string CandidateVariable = "BOOKINGTRACKER_INDEX_CANDIDATE";

    private async Task<string> ApplyCandidateAsync()
    {
        var choice = Environment.GetEnvironmentVariable(CandidateVariable);
        if (string.IsNullOrWhiteSpace(choice)) return "as migrated (no candidate applied)";

        var candidate = IndexCandidates.All.FirstOrDefault(
            c => c.Name.StartsWith(choice.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Unknown {CandidateVariable}='{choice}'. Known: {string.Join(", ", IndexCandidates.All.Select(c => c.Name[..1]))}");

        await IndexCandidates.ApplyAsync(candidate);
        return candidate.Name;
    }

    [DiagnosticFact]
    public async Task ContestedSlot()
    {
        await ConflictDataset.EnsureSeededAsync(output.WriteLine);
        var indexes = await ApplyCandidateAsync();
        Header("CONTESTED - every racer wants the same slot (1 winner is correct)", indexes);

        var measured = new List<(int Racers, ContentionHarness.Result Result)>();

        foreach (var racers in Levels)
        {
            for (var round = 1; round <= Rounds; round++)
            {
                var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(
                    db, email: Unique("contested") + "@example.com", slug: Unique("contested")));

                var date = TestData.NextBookableWeekday();
                var time = await BookingFlow.FirstAvailableTimeAsync(Client, workspace.Page.Slug, date);

                var prepared = await PrepareAsync(Enumerable.Repeat(workspace.Page.Slug, racers), date, time);
                var result = await ContentionHarness.RaceAsync(prepared);

                Report(racers, round, result);
                measured.Add((racers, result));
                Dispose(prepared);
            }
        }

        AssertContract(measured, expectedOk: racers => 1);
    }

    [DiagnosticFact]
    public async Task IndependentSlots()
    {
        await ConflictDataset.EnsureSeededAsync(output.WriteLine);
        var indexes = await ApplyCandidateAsync();
        Header("INDEPENDENT - every racer books a different organizer (all should succeed)", indexes);

        var measured = new List<(int Racers, ContentionHarness.Result Result)>();

        foreach (var racers in Levels)
        {
            for (var round = 1; round <= Rounds; round++)
            {
                var slugs = new List<string>();
                var date = TestData.NextBookableWeekday();

                for (var i = 0; i < racers; i++)
                {
                    var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(
                        db, email: Unique("solo") + "@example.com", slug: Unique("solo")));
                    slugs.Add(workspace.Page.Slug);
                }

                // Each racer takes the same clock time on its OWN organizer's
                // page. Different organizers entirely, so nothing here is a
                // conflict the domain recognises.
                var time = await BookingFlow.FirstAvailableTimeAsync(Client, slugs[0], date);
                var prepared = await PrepareAsync(slugs, date, time);
                var result = await ContentionHarness.RaceAsync(prepared);

                Report(racers, round, result);
                measured.Add((racers, result));
                Dispose(prepared);
            }
        }

        AssertContract(measured, expectedOk: racers => racers);
    }

    /// <summary>
    /// Asserted once, over the whole grid, rather than per round - so a failure
    /// at four racers cannot hide what sixteen would have shown. The numbers are
    /// already printed by then, which is the point.
    /// </summary>
    private void AssertContract(
        IReadOnlyList<(int Racers, ContentionHarness.Result Result)> measured, Func<int, int> expectedOk)
    {
        var problems = new List<string>();

        foreach (var (racers, result) in measured)
        {
            var ok = expectedOk(racers);
            if (result.Ok != ok || result.Conflicts != racers - ok || result.Other != 0 || result.ServerErrors != 0)
            {
                problems.Add(
                    $"{racers} racers: expected {ok} ok / {racers - ok} conflict, got " +
                    $"{result.Ok} ok / {result.Conflicts} conflict / {result.ServerErrors} 5xx / {result.Other} other" +
                    (result.Unexpected.Any() ? " -> " + string.Join(" | ", result.Unexpected) : string.Empty));
            }
        }

        if (problems.Count > 0)
        {
            foreach (var problem in problems) output.WriteLine("  !! " + problem);
            Assert.Fail(string.Join(Environment.NewLine, problems));
        }
    }

    // ---- plumbing -----------------------------------------------------------

    private async Task<IReadOnlyList<ContentionHarness.Racer>> PrepareAsync(
        IEnumerable<string> slugs, DateOnly date, TimeOnly time)
    {
        var racers = new List<ContentionHarness.Racer>();
        var index = 0;

        foreach (var slug in slugs)
        {
            var client = Factory.CreateClient();
            var sessionId = await BookingFlow.StartSessionAsync(client, slug);
            await BookingFlow.FillAsync(client, sessionId, date, time,
                $"Racer {index}", $"racer{index}@example.com");
            racers.Add(new ContentionHarness.Racer(client, sessionId));
            index++;
        }

        return racers;
    }

    private static void Dispose(IReadOnlyList<ContentionHarness.Racer> racers)
    {
        foreach (var racer in racers) racer.Client.Dispose();
    }

    private void Header(string title, string indexes) => output.WriteLine($"""

        {title}
        Indexes: {indexes}
        ----------------------------------------------------------------------------
        racers round     ok  409  5xx  deadlocks    wall      p50      p95      max
        """);

    private void Report(int racers, int round, ContentionHarness.Result result) =>
        output.WriteLine(
            $"{racers,6} {round,5} {result.Ok,6} {result.Conflicts,4} {result.ServerErrors,4} " +
            $"{result.Deadlocks,10} {result.WallClockMs,7:F0}ms {result.P50,6:F0}ms " +
            $"{result.P95,6:F0}ms {result.Max,6:F0}ms");
}
