using BookingTracker.Domain.Entities;
using BookingTracker.Domain.ValueObjects;
using BookingTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.SqlServerTests.Infrastructure;

/// <summary>
/// Puts enough booking history in the table for SQL Server's plan choice to be
/// the one a real installation gets.
///
/// Needed by exactly one test, and the reason is worth stating rather than
/// hiding behind a call: a cost-based optimizer scans a table that fits in two
/// pages, because there a scan IS cheapest - and a scan under Serializable
/// key-range-locks every row it read, which on a two-page table is every row
/// there is. So on a nearly empty database two unrelated bookings still collide,
/// no matter how the indexes are shaped, and a concurrency test run there
/// measures the table's size rather than the product's behaviour.
///
/// Measured on this schema (Diagnostics/LockFootprintByScale): the access path
/// flips from an index scan to an index seek somewhere between 200 and 1,000
/// booking sessions, and the key-range lock count drops from "every row" to a
/// handful with it. <see cref="Minimum"/> is set past that with room to spare,
/// and is still a trivial amount of data for any real organizer.
/// </summary>
public static class BookingVolume
{
    public const int Minimum = 1_500;

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _seeded;

    public static async Task EnsureAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (_seeded) return;

            await using var db = SqlServerDatabaseFixture.CreateContext();

            var existing = await db.BookingSessions.CountAsync();
            var wanted = Minimum - existing;

            if (wanted > 0) await SeedAsync(db, wanted);

            // Without this the plan is still costed from statistics gathered
            // when the table was empty, so the rows would be there and the seek
            // would not.
            await db.Database.ExecuteSqlRawAsync("UPDATE STATISTICS [BookingSessions] WITH FULLSCAN;");
            await db.Database.ExecuteSqlRawAsync("UPDATE STATISTICS [BookingPages] WITH FULLSCAN;");

            _seeded = true;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Bookings belonging to their own organizer, spread across ninety dates -
    /// history, not a pile of rows on one day. Nothing else in the suite can see
    /// them, since every test scopes its assertions to its own organizer.
    /// </summary>
    private static async Task SeedAsync(BookingTrackerDbContext db, int count)
    {
        var organizer = Organizer.Register(
            "Background volume", $"volume-{Guid.NewGuid():N}@sqltests.invalid", "not-a-hash");
        db.Organizers.Add(organizer);

        var pages = Enumerable.Range(0, 3)
            .Select(i => BookingPage.Create(organizer.Id, $"volume-{Guid.NewGuid():N}", $"Volume {i}", 30, 0, 0))
            .ToArray();
        db.BookingPages.AddRange(pages);
        await db.SaveChangesAsync();

        db.ChangeTracker.AutoDetectChangesEnabled = false;

        var context = new ClientContext("127.0.0.1", "sqltests");
        var firstDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(120);
        var batch = new List<BookingSession>();

        for (var i = 0; i < count; i++)
        {
            var (session, _) = BookingSession.Start(pages[i % pages.Length].Id, context);
            session.ChangeField("Name", $"Volume {i}", 1, context);
            session.ChangeField("Email", $"volume{i}@sqltests.invalid", 2, context);
            session.SelectDate(firstDate.AddDays(i % 90), 3, context);
            session.SelectTime(new TimeOnly(9, 0).AddMinutes(15 * (i % 32)), 4, context);
            session.Submit(5, context);
            batch.Add(session);

            if (batch.Count < 500) continue;

            await FlushAsync(db, batch);
        }

        await FlushAsync(db, batch);
        db.ChangeTracker.AutoDetectChangesEnabled = true;
    }

    private static async Task FlushAsync(BookingTrackerDbContext db, List<BookingSession> batch)
    {
        if (batch.Count == 0) return;
        db.BookingSessions.AddRange(batch);
        db.ChangeTracker.DetectChanges();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        batch.Clear();
    }
}
