using BookingTracker.SqlServerTests.Infrastructure;
using Xunit.Abstractions;

namespace BookingTracker.SqlServerTests.Diagnostics;

/// <summary>
/// Investigation steps 3 and 5: what SQL Server does with the booking
/// conflict query, and what it locks while doing it.
///
/// Reports rather than asserts - see <see cref="DiagnosticFactAttribute"/>. The
/// one thing it does assert is that the query being measured is still the query
/// production runs.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class ConflictQueryDiagnostics(ITestOutputHelper output)
{
    [DiagnosticFact]
    public async Task PlanReadsAndLockFootprintOfTheConflictQuery()
    {
        var shape = await ConflictDataset.EnsureSeededAsync(output.WriteLine);

        output.WriteLine($"""

            Dataset
            -------
            organizers        {shape.Organizers}
            pages/organizer   {shape.PagesPerOrganizer}
            dates             {shape.Dates}
            sessions          {shape.Sessions}
            target organizer  {shape.TargetOrganizerId}
            target page       {shape.TargetPageId}
            target date       {shape.TargetDate:yyyy-MM-dd}
            """);

        await ConflictQuery.AssertMatchesProductionAsync(
            shape.TargetOrganizerId, shape.TargetPageId, Guid.NewGuid(),
            shape.TargetDate, shape.FreeTime, output.WriteLine);

        var sql = ConflictQuery.Sql(shape.TargetOrganizerId, Guid.NewGuid(), shape.TargetDate);
        output.WriteLine($"""

            Generated SQL
            -------------
            {sql}
            """);

        var rowCounts = await ConflictRowCounts.MeasureAsync(shape);
        output.WriteLine($"""

            Selectivity of each predicate on its own
            ----------------------------------------
            BookingSessions total                              {rowCounts.Total}
            .. Status = Submitted                              {rowCounts.Submitted}
            .. Status = Submitted AND SelectedDate = target     {rowCounts.SubmittedOnDate}
            .. .. AND organizer = target  (what the query wants) {rowCounts.SubmittedOnDateForOrganizer}
            """);

        var plan = await SqlServerProbe.ActualPlanAsync(sql);
        output.WriteLine($"""

            Actual execution plan
            ---------------------
            {plan}
            """);

        var reads = await SqlServerProbe.LogicalReadsAsync(sql);
        output.WriteLine($"""

            Logical reads
            -------------
            {string.Join(Environment.NewLine, reads)}
            """);

        var timings = await SqlServerProbe.TimeAsync(sql, runs: 20);
        Array.Sort(timings);
        output.WriteLine($"""

            Elapsed (20 runs, warm)
            -----------------------
            min {timings[0]:F2} ms   p50 {timings[timings.Length / 2]:F2} ms   max {timings[^1]:F2} ms
            """);

        await ReportLocksAsync(sql);
    }

    /// <summary>
    /// The same three measurements again, once per candidate index shape, with
    /// the candidate applied to the throwaway database and then reverted.
    ///
    /// Run after the baseline above and read beside it - the numbers only mean
    /// something as a comparison.
    /// </summary>
    [DiagnosticFact]
    public async Task CandidateIndexShapes()
    {
        var shape = await ConflictDataset.EnsureSeededAsync(output.WriteLine);
        var sql = ConflictQuery.Sql(shape.TargetOrganizerId, Guid.NewGuid(), shape.TargetDate);

        output.WriteLine($"""

            Index sizes before any change
            -----------------------------
            {string.Join(Environment.NewLine, await IndexCandidates.SizesAsync("BookingSessions"))}
            """);

        foreach (var candidate in IndexCandidates.All)
        {
            await IndexCandidates.ApplyAsync(candidate);
            try
            {
                output.WriteLine($"""

                    ==========================================================================
                    {candidate.Name}
                    {candidate.Description}
                    ==========================================================================

                    Plan
                    ----
                    {await SqlServerProbe.ActualPlanAsync(sql)}

                    Logical reads
                    -------------
                    {string.Join(Environment.NewLine, (await SqlServerProbe.LogicalReadsAsync(sql))
                        .Where(line => line.Contains("BookingSessions") || line.Contains("BookingPages")))}
                    """);

                var timings = await SqlServerProbe.TimeAsync(sql, runs: 20);
                Array.Sort(timings);
                output.WriteLine(
                    $"\nElapsed (20 runs, warm): min {timings[0]:F2} ms   " +
                    $"p50 {timings[timings.Length / 2]:F2} ms   max {timings[^1]:F2} ms");

                await ReportLocksAsync(sql);

                output.WriteLine($"""

                    Index sizes with this candidate applied
                    --------------------------------------
                    {string.Join(Environment.NewLine, await IndexCandidates.SizesAsync("BookingSessions"))}
                    """);
            }
            finally
            {
                await IndexCandidates.RevertAsync(candidate);
            }
        }
    }

    private async Task ReportLocksAsync(string sql)
    {
        output.WriteLine("""

            Locks held under SERIALIZABLE, with the query's transaction still open
            ----------------------------------------------------------------------
            """);

        foreach (var held in await SqlServerProbe.RangeLocksAsync(sql))
        {
            output.WriteLine($"  {held.ResourceType,-10} {held.Mode,-12} {held.Index ?? "-",-58} x{held.Count}");
        }
    }
}
