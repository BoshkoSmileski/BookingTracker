using System.Net;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.IntegrationTests.Infrastructure;
using BookingTracker.SqlServerTests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.SqlServerTests;

/// <summary>
/// The constraints that are only constraints on SQL Server.
///
/// **The EF Core InMemory provider enforces primary keys and nothing else.** It
/// ignores unique indexes entirely, ignores index filters, ignores column
/// lengths, and ignores cascade behaviour - so every "the database physically
/// cannot hold this" claim is, in the other three suites, an
/// unverified comment. Each test here is one of those claims, checked against a
/// real database.
///
/// Only invariants are covered. There is no test that a column is nullable
/// because it is declared nullable; a constraint earns a test by protecting
/// something the application would otherwise get wrong.
/// </summary>
public class DatabaseConstraintTests : SqlServerApiTestBase
{
    /// <summary>
    /// A lead time no booking arrives with. Confirming a booking already
    /// schedules the default 24-hour (1440) reminder, so a test that used 1440
    /// would be colliding with that row rather than with the one it inserted -
    /// and would pass for the wrong reason in the "a queued one does not block a
    /// fresh one" case.
    /// </summary>
    private const int UnusedLeadMinutes = 123;

    /// <summary>SQL Server error 2601/2627: duplicate key in a unique index / unique constraint.</summary>
    private static async Task AssertRejectsDuplicateAsync(Func<Task> save, string index)
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(save);
        var sql = Assert.IsType<SqlException>(exception.GetBaseException());

        Assert.True(sql.Number is 2601 or 2627,
            $"Expected a duplicate-key violation from {index}, got SQL Server error {sql.Number}: {sql.Message}");
    }

    // ---- The never-send-a-reminder-twice guarantee ---------------------------

    [Fact]
    public async Task TwoPendingRemindersForTheSameBookingAndLeadTimeAreImpossible()
    {
        // UX_BookingReminders_Session_Offset_Scheduled. This is THE
        // duplicate-delivery guarantee, and it survives
        // restarts and overlapping sweeps precisely because it is a database
        // constraint rather than application care - which is a claim only a
        // database can answer.
        var session = await ASubmittedBookingAsync();

        await using var db = SqlServerDatabaseFixture.CreateContext();
        db.BookingReminders.Add(BookingReminder.Schedule(session.Id, session.BookingPageId, UnusedLeadMinutes, DateTime.UtcNow.AddDays(2)));
        await db.SaveChangesAsync();

        db.BookingReminders.Add(BookingReminder.Schedule(session.Id, session.BookingPageId, UnusedLeadMinutes, DateTime.UtcNow.AddDays(2)));

        await AssertRejectsDuplicateAsync(() => db.SaveChangesAsync(), "UX_BookingReminders_Session_Offset_Scheduled");
    }

    [Fact]
    public async Task AnAlreadyQueuedReminderDoesNotBlockAFreshOneForTheSameLeadTime()
    {
        // The filter is the load-bearing half, and the half a plain unique index
        // would get wrong: after a reschedule a booking legitimately holds an
        // already-sent 24h reminder for the old time AND a scheduled one for the
        // new time. Those are two different meetings, not a duplicate.
        var session = await ASubmittedBookingAsync();

        await using var db = SqlServerDatabaseFixture.CreateContext();

        var alreadySent = BookingReminder.Schedule(session.Id, session.BookingPageId, UnusedLeadMinutes, DateTime.UtcNow.AddDays(2));
        alreadySent.MarkQueued(Guid.NewGuid());
        db.BookingReminders.Add(alreadySent);
        await db.SaveChangesAsync();

        db.BookingReminders.Add(BookingReminder.Schedule(session.Id, session.BookingPageId, UnusedLeadMinutes, DateTime.UtcNow.AddDays(5)));
        await db.SaveChangesAsync();

        var rows = await db.BookingReminders.CountAsync(
            r => r.BookingSessionId == session.Id && r.MinutesBeforeEvent == UnusedLeadMinutes);
        Assert.Equal(2, rows);
    }

    // ---- One-of-a-kind rows -------------------------------------------------

    [Fact]
    public async Task TwoBookingPagesCannotShareASlug()
    {
        // A slug is the public booking URL. Two pages sharing one would make
        // GET /api/booking-pages/{slug} answer with whichever row came back
        // first - an availability answer that depends on row order.
        var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(
            db, email: Unique("slug") + "@example.com", slug: Unique("taken-slug")));

        await using var db2 = SqlServerDatabaseFixture.CreateContext();
        db2.BookingPages.Add(BookingPage.Create(workspace.Organizer.Id, workspace.Page.Slug, "Impostor", 30, 0, 0));

        await AssertRejectsDuplicateAsync(() => db2.SaveChangesAsync(), "IX_BookingPages_Slug");
    }

    [Fact]
    public async Task TwoOrganizersCannotShareAnEmailAddress()
    {
        var email = Unique("dup") + "@example.com";
        await WithDbAsync(db => TestData.AddOrganizerAsync(db, email));

        await using var db = SqlServerDatabaseFixture.CreateContext();
        db.Organizers.Add(Organizer.Register("Second", email, "hash"));

        await AssertRejectsDuplicateAsync(() => db.SaveChangesAsync(), "IX_Organizers_Email");
    }

    [Fact]
    public async Task AnOrganizerCannotHaveTwoOverridesForOneDate()
    {
        // The reason the write side is a single upsert rather than a
        // Create/Update pair: SlotGenerationService.ResolveOpenRanges takes the
        // first match, so two rows for one date would make a day's opening hours
        // depend on row order.
        var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(
            db, email: Unique("override") + "@example.com", slug: Unique("override-page")));
        var date = TestData.NextBookableWeekday();

        await using var db = SqlServerDatabaseFixture.CreateContext();
        db.AvailabilityOverrides.Add(AvailabilityOverride.Create(workspace.Organizer.Id, date, [], note: "first"));
        await db.SaveChangesAsync();

        db.AvailabilityOverrides.Add(AvailabilityOverride.Create(workspace.Organizer.Id, date, [], note: "second"));

        await AssertRejectsDuplicateAsync(() => db.SaveChangesAsync(), "IX_AvailabilityOverrides_OrganizerId_Date");
    }

    [Fact]
    public async Task AWorkingScheduleCannotHaveTheSameWeekdayTwice()
    {
        var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(
            db, email: Unique("weekday") + "@example.com", slug: Unique("weekday-page")));

        await using var db = SqlServerDatabaseFixture.CreateContext();
        db.WorkingDays.Add(WorkingDay.Create(workspace.Schedule.Id, DayOfWeek.Monday, true, []));

        await AssertRejectsDuplicateAsync(() => db.SaveChangesAsync(), "IX_WorkingDays_WorkingScheduleId_DayOfWeek");
    }

    // ---- Column limits, and why the validator has to get there first --------

    [Fact]
    public async Task AnOverlongAnswerIsRefusedByTheValidatorBeforeSqlServerEverSeesIt()
    {
        // A SqlException reaching the client is always a
        // bug. The InMemory provider ignores HasMaxLength, so in every other
        // suite this test would pass even with the validator deleted - the
        // oversized value would simply be stored. Here the column is real, so
        // the 400 is the validator's and nothing else's.
        var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(
            db, email: Unique("limits") + "@example.com", slug: Unique("limits-page")));

        var sessionId = await BookingFlow.StartSessionAsync(Client, workspace.Page.Slug);
        var response = await BookingFlow.AppendEventsAsync(Client, sessionId,
            new BookingFlow.ClientEvent("FieldChanged", "Name", new string('x', 500), 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // And the projection is untouched - the request did not get half-applied.
        var name = await WithDbAsync(db => db.BookingSessions.Where(s => s.Id == sessionId).Select(s => s.Name).SingleAsync());
        Assert.True(string.IsNullOrEmpty(name), $"Expected no name to have been recorded, got \"{name}\".");
    }

    [Fact]
    public async Task TheNameColumnWouldHaveRejectedItAnyway()
    {
        // The other half of the rule, and what makes the test above mean
        // something: the limit is a real column constraint, so if the validator
        // ever stopped enforcing it the failure would be a 500 rather than
        // silent truncation. Written directly against the database, because
        // there is no way to reach this through the API - which is the point.
        var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(
            db, email: Unique("column") + "@example.com", slug: Unique("column-page")));
        var sessionId = await BookingFlow.StartSessionAsync(Client, workspace.Page.Slug);

        await using var db = SqlServerDatabaseFixture.CreateContext();
        var exception = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE BookingSessions SET Name = {0} WHERE Id = {1}", new string('x', 500), sessionId));

        // 8152 (pre-2016 compat) / 2628 - "String or binary data would be truncated".
        Assert.True(exception.Number is 8152 or 2628,
            $"Expected a truncation error from the Name column, got {exception.Number}: {exception.Message}");
    }

    // ---- Cascades -----------------------------------------------------------

    [Fact]
    public async Task DeletingABookingSessionTakesItsWholeHistoryWithIt()
    {
        // Four cascade paths converge on BookingSessions - events, answers,
        // reminders and queued emails. Two of the FKs elsewhere in the model are
        // Restrict specifically because SQL Server rejects a second cascade path
        // into the same table, so what actually happens on a delete is a
        // property of the real database and of nothing else.
        var session = await ASubmittedBookingAsync();

        await using (var seed = SqlServerDatabaseFixture.CreateContext())
        {
            Assert.True(await seed.BookingSessionEvents.AnyAsync(e => e.SessionId == session.Id));
            Assert.True(await seed.EmailNotifications.AnyAsync(n => n.BookingSessionId == session.Id));
            Assert.True(await seed.BookingReminders.AnyAsync(r => r.BookingSessionId == session.Id));
        }

        await using var db = SqlServerDatabaseFixture.CreateContext();
        db.BookingSessions.Remove(await db.BookingSessions.SingleAsync(s => s.Id == session.Id));
        await db.SaveChangesAsync();

        Assert.False(await db.BookingSessionEvents.AnyAsync(e => e.SessionId == session.Id));
        Assert.False(await db.EmailNotifications.AnyAsync(n => n.BookingSessionId == session.Id));
        Assert.False(await db.BookingReminders.AnyAsync(r => r.BookingSessionId == session.Id));
    }

    [Fact]
    public async Task DeletingABookingPageWithAnAbandonedSessionSucceedsOnRealSqlServer()
    {
        // The delete is refused for a page with CONFIRMED bookings, by the
        // handler. A page with an in-progress or abandoned session is allowed
        // through - and that path crosses two FKs into BookingSessionEvents at
        // once: a cascade via BookingSessions, and a Restrict directly from
        // BookingPageId. Whether SQL Server accepts that ordering is not
        // something the InMemory provider can answer, and a 500 on deleting an
        // unused page would be a real defect.
        var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(
            db, email: Unique("delete") + "@example.com", slug: Unique("delete-page")));
        using var organizer = ClientFor(workspace.Organizer);

        var sessionId = await BookingFlow.StartSessionAsync(Client, workspace.Page.Slug);
        await BookingFlow.FillAsync(Client, sessionId, TestData.NextBookableWeekday(), new TimeOnly(9, 0));

        var response = await organizer.DeleteAsync($"/api/organizer/booking-pages/{workspace.Page.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await WithDbAsync(async db =>
        {
            Assert.False(await db.BookingPages.AnyAsync(p => p.Id == workspace.Page.Id));
            Assert.False(await db.BookingSessions.AnyAsync(s => s.Id == sessionId));
            Assert.False(await db.BookingSessionEvents.AnyAsync(e => e.SessionId == sessionId));
        });
    }

    // ---- helpers ------------------------------------------------------------

    /// <summary>
    /// A real confirmed booking, made over HTTP - so it arrives with the event
    /// log, the queued emails and the scheduled reminder a booking really has,
    /// rather than with whatever subset a hand-built row would remember.
    /// </summary>
    private async Task<BookingSession> ASubmittedBookingAsync()
    {
        var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(
            db, email: Unique("seed") + "@example.com", slug: Unique("seed-page")));

        var date = TestData.NextBookableWeekday();
        var time = await BookingFlow.FirstAvailableTimeAsync(Client, workspace.Page.Slug, date);
        var confirmation = await BookingFlow.BookAsync(Client, workspace.Page.Slug, date, time);
        var reference = confirmation.GetProperty("bookingReference").GetString();

        return await WithDbAsync(db => db.BookingSessions
            .AsNoTracking()
            .SingleAsync(s => s.BookingReference == reference && s.Status == BookingSessionStatus.Submitted));
    }
}
