using BookingTracker.Infrastructure.Auth;
using BookingTracker.Infrastructure.Persistence;
using BookingTracker.Infrastructure.Persistence.Seed;
using BookingTracker.SqlServerTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.SqlServerTests;

/// <summary>
/// The startup seeder's re-entrancy, against the unique indexes that only exist
/// on a real database.
///
/// This belongs here rather than in the unit suite for the reason
/// <see cref="DatabaseConstraintTests"/> gives: the InMemory provider ignores
/// unique indexes, so with the defect below it would happily hold a *second*
/// demo organizer instead of throwing, and the measured production symptom - the
/// API refusing to start at all - would be unreproducible.
///
/// Every query is scoped to <see cref="DevelopmentSeeder.DemoOrganizerEmail"/>,
/// since the whole assembly shares one database, and each test starts by
/// clearing the demo rows so none of them depends on the order they run in.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class DevelopmentSeederTests
{
    private static async Task RunSeederAsync()
    {
        await using var db = SqlServerDatabaseFixture.CreateContext();
        await DevelopmentSeeder.SeedAsync(db, new PasswordHasher());
    }

    /// <summary>Removes only what the seeder creates, so the shared database is left as it was found.</summary>
    private static async Task ClearDemoRowsAsync()
    {
        await using var db = SqlServerDatabaseFixture.CreateContext();
        await RemoveDemoPageAsync(db);

        var organizer = await db.Organizers.SingleOrDefaultAsync(o => o.Email == DevelopmentSeeder.DemoOrganizerEmail);
        if (organizer is null) return;

        var schedules = await db.WorkingSchedules.Where(s => s.OrganizerId == organizer.Id).ToListAsync();
        var scheduleIds = schedules.Select(s => s.Id).ToList();
        db.WorkingDays.RemoveRange(await db.WorkingDays.Where(d => scheduleIds.Contains(d.WorkingScheduleId)).ToListAsync());
        db.WorkingSchedules.RemoveRange(schedules);
        db.Organizers.Remove(organizer);
        await db.SaveChangesAsync();
    }

    /// <summary>Exactly what DeleteBookingPageCommandHandler does: the page goes, the organizer stays.</summary>
    private static async Task RemoveDemoPageAsync(BookingTrackerDbContext db)
    {
        var page = await db.BookingPages.SingleOrDefaultAsync(p => p.Slug == DevelopmentSeeder.DemoBookingPageSlug);
        if (page is null) return;

        db.BookingPages.Remove(page);
        await db.SaveChangesAsync();
    }

    private static async Task<(int Organizers, int Pages, int Schedules, int Days)> DemoRowsAsync()
    {
        await using var db = SqlServerDatabaseFixture.CreateContext();
        var organizer = await db.Organizers
            .SingleOrDefaultAsync(o => o.Email == DevelopmentSeeder.DemoOrganizerEmail);

        var scheduleIds = organizer is null
            ? []
            : await db.WorkingSchedules.Where(s => s.OrganizerId == organizer.Id).Select(s => s.Id).ToListAsync();

        return (
            await db.Organizers.CountAsync(o => o.Email == DevelopmentSeeder.DemoOrganizerEmail),
            await db.BookingPages.CountAsync(p => p.Slug == DevelopmentSeeder.DemoBookingPageSlug),
            scheduleIds.Count,
            await db.WorkingDays.CountAsync(d => scheduleIds.Contains(d.WorkingScheduleId)));
    }

    /// <summary>
    /// Reproduces the seeder defect, in the state ordinary product use leaves
    /// behind.
    ///
    /// The demo booking page ships with no confirmed bookings, so the demo
    /// account the seeder itself creates can delete it - measured over HTTP as a
    /// plain 204. That leaves the organizer without the page, and the seeder's
    /// guard used to ask about the page while the insert was about the organizer.
    /// The next start therefore passed the guard, re-inserted
    /// organizer@example.com, and SQL Server answered 2601 on IX_Organizers_Email
    /// - which, because Program.cs seeds before app.Run(), killed the host before
    /// it listened, and kept doing so on every subsequent start.
    /// </summary>
    [Fact]
    public async Task SeedingIsSafeToRepeatAfterTheDemoBookingPageHasBeenDeleted()
    {
        await ClearDemoRowsAsync();

        await RunSeederAsync();
        var seeded = await DemoRowsAsync();
        Assert.Equal(1, seeded.Organizers);
        Assert.Equal(1, seeded.Pages);

        await using (var db = SqlServerDatabaseFixture.CreateContext())
        {
            await RemoveDemoPageAsync(db);
        }

        // The assertion is that this does not throw. A DbUpdateException here is
        // the API failing to start.
        await RunSeederAsync();

        var after = await DemoRowsAsync();
        Assert.Equal(1, after.Organizers);
        Assert.Equal(1, after.Schedules);
        Assert.Equal(7, after.Days);
    }

    /// <summary>
    /// The plain idempotency claim the guard has to keep holding: further starts
    /// against an already-seeded database add nothing. Measured against the real
    /// unique indexes, so "no duplicate" is the database's answer rather than a
    /// provider that cannot express one.
    /// </summary>
    [Fact]
    public async Task RepeatedSeedingOnAnAlreadySeededDatabaseAddsNothing()
    {
        await ClearDemoRowsAsync();

        await RunSeederAsync();
        await RunSeederAsync();
        await RunSeederAsync();

        var rows = await DemoRowsAsync();
        Assert.Equal(1, rows.Organizers);
        Assert.Equal(1, rows.Pages);
        Assert.Equal(1, rows.Schedules);
        Assert.Equal(7, rows.Days);
    }

    /// <summary>
    /// The scope of what seeding creates, pinned because it is the answer to
    /// "can startup manufacture anything externally meaningful". It creates a
    /// login and a bookable page - deliberately; that is the point of it - and
    /// nothing else: no refresh token, no booking session, no queued email, no
    /// reminder and no calendar connection. So nothing it writes is addressable
    /// by a public token, and nothing it writes is queued for delivery.
    /// </summary>
    [Fact]
    public async Task SeedingCreatesNoTokensSessionsEmailsRemindersOrCalendarState()
    {
        await ClearDemoRowsAsync();
        await RunSeederAsync();

        await using var db = SqlServerDatabaseFixture.CreateContext();
        var organizer = await db.Organizers.SingleAsync(o => o.Email == DevelopmentSeeder.DemoOrganizerEmail);
        var page = await db.BookingPages.SingleAsync(p => p.Slug == DevelopmentSeeder.DemoBookingPageSlug);

        Assert.Equal(0, await db.RefreshTokens.CountAsync(t => t.OrganizerId == organizer.Id));
        Assert.Equal(0, await db.CalendarConnections.CountAsync(c => c.OrganizerId == organizer.Id));
        Assert.Equal(0, await db.NotificationSettings.CountAsync(n => n.OrganizerId == organizer.Id));
        Assert.Equal(0, await db.BookingSessions.CountAsync(s => s.BookingPageId == page.Id));
        Assert.Equal(0, await db.BookingReminders.CountAsync(r => r.BookingPageId == page.Id));
        Assert.Equal(0, await db.EmailNotifications.CountAsync(n => n.ToEmail == DevelopmentSeeder.DemoOrganizerEmail));
    }
}
