using BookingTracker.Domain.Entities;
using BookingTracker.Domain.ValueObjects;
using BookingTracker.SqlServerTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace BookingTracker.SqlServerTests.Diagnostics;

/// <summary>
/// At what table size does the index actually change SQL Server's mind?
///
/// This exists because the first run of the new regression test failed on a
/// nearly empty database, with the index in place. That is not a contradiction
/// and it is not a flake: a cost-based optimizer scans a table that fits in a
/// couple of pages, because a scan really is cheaper there - and a scan under
/// Serializable locks everything it read, which on a tiny table is the whole
/// table.
///
/// The point worth measuring is therefore not "does the index help" but "from
/// what size", because that is the difference between a fix and a fix with a
/// caveat. Reported per size: the access path chosen, and whether a table-level
/// S lock (rather than key-range locks) was taken - the latter being the thing
/// that makes two unrelated bookings deadlock.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class LockFootprintByScale(ITestOutputHelper output)
{
    private static readonly int[] Sizes = [50, 200, 1_000, 4_000, 16_000];

    [DiagnosticFact]
    public async Task WhenTheIndexStartsBeingUsed()
    {
        var (organizerId, pages, date) = await PrepareAsync();

        output.WriteLine("""

              rows  access path                                                     table-lock  key-range  reads
            ------  --------------------------------------------------------------  ----------  ---------  -----
            """);

        var seeded = 0;

        foreach (var size in Sizes)
        {
            seeded += await SeedAsync(pages, date, from: seeded, to: size);
            await UpdateStatisticsAsync();

            var sql = ConflictQuery.Sql(organizerId, Guid.NewGuid(), date);

            var plan = await SqlServerProbe.ActualPlanAsync(sql);
            var access = plan.Operators
                .Where(op => op.Index?.StartsWith("BookingSessions", StringComparison.Ordinal) == true)
                .Select(op => $"{op.Physical} on {op.Index!["BookingSessions.".Length..]}")
                .DefaultIfEmpty("(none)")
                .ToArray();

            var locks = await SqlServerProbe.RangeLocksAsync(sql);
            var tableLock = locks.Any(l =>
                l.ResourceType == "OBJECT" && l.Mode == "S" && l.Index == "BookingSessions");
            var rangeLocks = locks
                .Where(l => l.ResourceType == "KEY" && l.Index?.StartsWith("BookingSessions") == true)
                .Sum(l => l.Count);

            var reads = (await SqlServerProbe.LogicalReadsAsync(sql))
                .FirstOrDefault(line => line.Contains("'BookingSessions'"))
                ?.Split("logical reads ")[1].Split(',')[0] ?? "?";

            output.WriteLine(
                $"{seeded,6}  {string.Join(" + ", access),-62}  " +
                $"{(tableLock ? "YES" : "no"),-10}  {rangeLocks,9}  {reads,5}");
        }

        output.WriteLine("""

            A "YES" in the table-lock column means every concurrent booking in the
            system serialises against every other, whatever it is for.
            """);
    }

    // ---- fixture ------------------------------------------------------------

    private static async Task<(Guid OrganizerId, IReadOnlyList<BookingPage> Pages, DateOnly Date)> PrepareAsync()
    {
        await using var db = SqlServerDatabaseFixture.CreateContext();

        var organizer = Organizer.Register("Scale sweep", $"scale-{Guid.NewGuid():N}@diagnostics.invalid", "not-a-hash");
        db.Organizers.Add(organizer);

        var pages = Enumerable.Range(0, 3)
            .Select(i => BookingPage.Create(organizer.Id, $"scale-{Guid.NewGuid():N}", $"Scale {i}", 30, 0, 0))
            .ToArray();
        db.BookingPages.AddRange(pages);

        await db.SaveChangesAsync();

        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        return (organizer.Id, pages, date);
    }

    /// <summary>
    /// Grows the table to <paramref name="to"/> rows, spread over many dates so
    /// the target date stays a small fraction of the whole - which is the shape
    /// real booking data has, and the shape that makes a seek worth choosing.
    /// </summary>
    private static async Task<int> SeedAsync(IReadOnlyList<BookingPage> pages, DateOnly date, int from, int to)
    {
        await using var db = SqlServerDatabaseFixture.CreateContext();
        db.ChangeTracker.AutoDetectChangesEnabled = false;

        var context = new ClientContext("127.0.0.1", "diagnostics");
        var batch = new List<BookingSession>();

        for (var i = from; i < to; i++)
        {
            var page = pages[i % pages.Count];
            var day = i % 90 == 0 ? date : date.AddDays(1 + i % 89);
            var time = new TimeOnly(9, 0).AddMinutes(15 * (i % 32));

            var (session, _) = BookingSession.Start(page.Id, context);
            session.ChangeField("Name", $"Scale {i}", 1, context);
            session.ChangeField("Email", $"scale{i}@diagnostics.invalid", 2, context);
            session.SelectDate(day, 3, context);
            session.SelectTime(time, 4, context);
            session.Submit(5, context);
            batch.Add(session);

            if (batch.Count >= 2000)
            {
                db.BookingSessions.AddRange(batch);
                db.ChangeTracker.DetectChanges();
                await db.SaveChangesAsync();
                db.ChangeTracker.Clear();
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            db.BookingSessions.AddRange(batch);
            db.ChangeTracker.DetectChanges();
            await db.SaveChangesAsync();
        }

        return to - from;
    }

    /// <summary>
    /// Without this the optimizer is costing the plan from statistics gathered
    /// at the previous size, so each step would report the decision the step
    /// before it would have made.
    /// </summary>
    private static async Task UpdateStatisticsAsync()
    {
        await using var db = SqlServerDatabaseFixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync("UPDATE STATISTICS [BookingSessions] WITH FULLSCAN;");
        await db.Database.ExecuteSqlRawAsync("UPDATE STATISTICS [BookingPages] WITH FULLSCAN;");
    }
}
