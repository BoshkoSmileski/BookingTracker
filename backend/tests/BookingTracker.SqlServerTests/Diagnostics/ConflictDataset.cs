using System.Diagnostics;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.ValueObjects;
using BookingTracker.Infrastructure.Persistence;
using BookingTracker.SqlServerTests.Infrastructure;

namespace BookingTracker.SqlServerTests.Diagnostics;

/// <summary>
/// A booking history large enough for the difference between a seek and a scan
/// to be visible.
///
/// The point of the size is not realism for its own sake. On a handful of rows
/// every access path costs one page read and every plan looks identical, so a
/// measurement taken there cannot support or refute an index. The shape matters
/// as much as the count:
///
///  - **Several organizers sharing the same dates.** This is what separates the
///    two candidate access paths. The conflict query filters BookingSessions by
///    Status and SelectedDate and only then narrows to one organizer, by joining
///    through BookingPages - BookingSessions has no OrganizerId column at all.
///    So an index led by (Status, SelectedDate) matches every organizer's
///    bookings on that date, and one led by BookingPageId matches only the
///    page's own. With a single organizer seeded, those two are the same set and
///    the whole question disappears.
///  - **Several pages per organizer**, because the rule is organizer-wide: a
///    conflict is decided across every page they own.
///  - **Non-submitted sessions mixed in**, since Status is the leading column of
///    the index the query is expected to use, and an index leading with a column
///    that has one value in practice is a different thing to measure.
///
/// The event log is deliberately NOT seeded. The conflict query never reads
/// BookingSessionEvents, and writing five events per session would multiply the
/// seeding cost for rows nothing under measurement touches. That makes this a
/// measurement fixture rather than a faithful application state - which is why
/// nothing here is reused by a correctness test.
/// </summary>
public static class ConflictDataset
{
    public sealed record Shape(
        Guid TargetOrganizerId,
        Guid TargetPageId,
        DateOnly TargetDate,
        TimeOnly FreeTime,
        int Organizers,
        int PagesPerOrganizer,
        int Dates,
        int Sessions);

    /// <summary>
    /// Seeds once per database. The fixture drops and recreates the database per
    /// run, so "once" means once per test run - and the marker organizer's email
    /// is what makes a second call in the same run a no-op rather than a second
    /// copy of the data.
    /// </summary>
    private const string MarkerEmail = "conflict-dataset-marker@diagnostics.invalid";

    public const int OrganizerCount = 6;
    public const int PagesPerOrganizer = 3;
    public const int DateCount = 60;

    /// <summary>Bookings per (page, date). 09:00-17:00 in 15-minute steps leaves room for this many.</summary>
    public const int BookingsPerPageDate = 15;

    public static async Task<Shape> EnsureSeededAsync(Action<string> log)
    {
        await using var db = SqlServerDatabaseFixture.CreateContext();

        var existing = await FindMarkerAsync(db);
        if (existing is not null)
        {
            log($"Dataset already seeded (marker organizer {existing.Id}).");
            return await DescribeAsync(db, existing);
        }

        var stopwatch = Stopwatch.StartNew();

        var marker = Organizer.Register("Conflict Dataset", MarkerEmail, "not-a-real-hash");
        db.Organizers.Add(marker);
        await db.SaveChangesAsync();

        var organizers = new List<Organizer> { marker };
        for (var i = 1; i < OrganizerCount; i++)
        {
            organizers.Add(Organizer.Register($"Dataset Organizer {i}", $"conflict-dataset-{i}@diagnostics.invalid", "not-a-real-hash"));
        }
        db.Organizers.AddRange(organizers.Skip(1));

        var pages = new List<BookingPage>();
        foreach (var (organizer, index) in organizers.Select((o, i) => (o, i)))
        {
            for (var p = 0; p < PagesPerOrganizer; p++)
            {
                pages.Add(BookingPage.Create(
                    organizer.Id, $"conflict-dataset-{index}-{p}", $"Dataset page {index}.{p}",
                    durationMinutes: 30, bufferBeforeMinutes: 0, bufferAfterMinutes: 0));
            }
        }
        db.BookingPages.AddRange(pages);
        await db.SaveChangesAsync();

        var targetDate = TargetDate();
        var firstDate = targetDate.AddDays(-DateCount / 2);
        var context = new ClientContext("127.0.0.1", "diagnostics");

        db.ChangeTracker.AutoDetectChangesEnabled = false;
        var pending = new List<BookingSession>();
        var total = 0;

        for (var d = 0; d < DateCount; d++)
        {
            var date = firstDate.AddDays(d);

            foreach (var page in pages)
            {
                for (var b = 0; b < BookingsPerPageDate; b++)
                {
                    // 09:00 + 15-minute steps. Slot 0 is left unbooked on every
                    // page so a probe has a free time to ask about, and the
                    // conflict query still has to look at all the others to
                    // decide that.
                    var time = new TimeOnly(9, 0).AddMinutes(15 * (b + 1));

                    var (session, _) = BookingSession.Start(page.Id, context);
                    session.ChangeField("Name", $"Guest {total}", 1, context);
                    session.ChangeField("Email", $"guest{total}@diagnostics.invalid", 2, context);
                    session.SelectDate(date, 3, context);
                    session.SelectTime(time, 4, context);

                    // One session in six stays un-submitted, so Status is not a
                    // constant across the table.
                    if (total % 6 != 0) session.Submit(5, context);

                    pending.Add(session);
                    total++;
                }
            }

            if (pending.Count >= 2000)
            {
                await FlushAsync(db, pending);
                log($"  seeded {total} sessions...");
            }
        }

        await FlushAsync(db, pending);
        db.ChangeTracker.AutoDetectChangesEnabled = true;

        log($"Seeded {total} booking sessions across {organizers.Count} organizers, " +
            $"{pages.Count} pages and {DateCount} dates in {stopwatch.Elapsed.TotalSeconds:F1}s.");

        return await DescribeAsync(db, marker);
    }

    private static async Task FlushAsync(BookingTrackerDbContext db, List<BookingSession> pending)
    {
        if (pending.Count == 0) return;
        db.BookingSessions.AddRange(pending);
        db.ChangeTracker.DetectChanges();
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        pending.Clear();
    }

    private static async Task<Organizer?> FindMarkerAsync(BookingTrackerDbContext db)
        => await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
            .FirstOrDefaultAsync(db.Organizers.AsQueryable(), o => o.Email == MarkerEmail);

    private static async Task<Shape> DescribeAsync(BookingTrackerDbContext db, Organizer marker)
    {
        var page = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstAsync(
            db.BookingPages.AsQueryable().Where(p => p.OrganizerId == marker.Id).OrderBy(p => p.Slug));

        var sessions = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(
            db.BookingSessions.AsQueryable());

        return new Shape(
            marker.Id, page.Id, TargetDate(), new TimeOnly(9, 0),
            OrganizerCount, PagesPerOrganizer, DateCount, sessions);
    }

    /// <summary>
    /// Derived from the clock rather than hardcoded: the
    /// concurrency benchmark books real slots through the real API, which applies
    /// the same-day cutoff and the booking window against DateTime.UtcNow.
    /// </summary>
    private static DateOnly TargetDate()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(21);
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) date = date.AddDays(1);
        return date;
    }
}
