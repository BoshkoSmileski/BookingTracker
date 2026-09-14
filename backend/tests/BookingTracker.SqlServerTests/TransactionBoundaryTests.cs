using System.Net;
using BookingTracker.Domain.Enums;
using BookingTracker.IntegrationTests.Infrastructure;
using BookingTracker.SqlServerTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.SqlServerTests;

/// <summary>
/// The two transaction boundaries in the application - the submit and the
/// reschedule claim - checked against a database that actually has
/// transactions.
///
/// The InMemory provider does not: <c>BookingTrackerApiFactory</c> downgrades
/// <c>TransactionIgnoredWarning</c> so those suites can run the same handlers at
/// all, which means everything they assert about a failed claim is really an
/// assertion about the handler having thrown before writing, not about anything
/// being rolled back.
///
/// **The strongest rollback evidence in this repository is in
/// <see cref="SlotConflictConcurrencyTests"/>, not here.** The deadlock victim
/// there fails *after* mutating its session and adding a BookingSubmitted event
/// to the transaction, and the assertion that the loser has no such event, no
/// booking reference and no public token is the proof that the rollback really
/// undid a write. What is left for this file is the deterministic half.
/// </summary>
public class TransactionBoundaryTests : SqlServerApiTestBase
{
    [Fact]
    public async Task ARefusedSubmitLeavesTheSessionExactlyAsItWas()
    {
        var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(
            db, email: Unique("txn") + "@example.com", slug: Unique("txn-page")));

        var date = TestData.NextBookableWeekday();
        var time = await BookingFlow.FirstAvailableTimeAsync(Client, workspace.Page.Slug, date);

        await BookingFlow.BookAsync(Client, workspace.Page.Slug, date, time, "First", "first@example.com");

        var latecomerId = await BookingFlow.StartSessionAsync(Client, workspace.Page.Slug);
        await BookingFlow.FillAsync(Client, latecomerId, date, time, "Second", "second@example.com");
        var response = await BookingFlow.SubmitAsync(Client, latecomerId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await WithDbAsync(async db =>
        {
            var latecomer = await db.BookingSessions.AsNoTracking().SingleAsync(s => s.Id == latecomerId);
            Assert.Equal(BookingSessionStatus.Active, latecomer.Status);
            Assert.Null(latecomer.SubmittedAt);
            Assert.Null(latecomer.BookingReference);
            Assert.Null(latecomer.PublicToken);

            Assert.False(await db.BookingSessionEvents
                .AnyAsync(e => e.SessionId == latecomerId && e.EventType == BookingEventType.BookingSubmitted));

            // None of the three best-effort blocks ran, because none of them is
            // reached until the claim commits.
            Assert.False(await db.EmailNotifications.AnyAsync(n => n.BookingSessionId == latecomerId));
            Assert.False(await db.BookingReminders.AnyAsync(r => r.BookingSessionId == latecomerId));
        });

        Assert.DoesNotContain(latecomerId, Factory.Calendar.Created);
    }

    [Fact]
    public async Task ARefusedRescheduleLeavesTheBookingOnItsOriginalSlot()
    {
        // The second transaction boundary, and the one where a partial write
        // would be worst: a booking is already live, so a half-applied
        // reschedule would move a real appointment that guests and calendars
        // have already been told about.
        var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(
            db, email: Unique("resched") + "@example.com", slug: Unique("resched-page")));

        var date = TestData.NextBookableWeekday();
        var slots = await BookingFlow.GetSlotsAsync(Client, workspace.Page.Slug, date, date);
        var first = TimeOnly.Parse(slots[0].GetProperty("localStartTime").GetString()!);

        // Slots are offered every 15 minutes but the page's appointments are 30
        // minutes long, so consecutive slots OVERLAP. Two bookings that can
        // coexist have to be a whole duration apart - taking slots[1] here would
        // make the setup itself a conflict and the test would never reach the
        // reschedule it is about.
        var second = first.AddMinutes(workspace.Page.DurationMinutes);
        Assert.Contains(second.ToString("HH:mm:ss"), slots.EnumerateArray()
            .Select(s => s.GetProperty("localStartTime").GetString()));

        var blocker = await BookingFlow.BookAsync(Client, workspace.Page.Slug, date, second, "Blocker", "blocker@example.com");
        var mover = await BookingFlow.BookAsync(Client, workspace.Page.Slug, date, first, "Mover", "mover@example.com");

        var token = mover.GetProperty("publicToken").GetString();
        var response = await Client.PostAsync($"/api/bookings/{token}/reschedule", RawJson(
            $$"""{"newDate":"{{date:yyyy-MM-dd}}","newTime":"{{second:HH\:mm\:ss}}"}"""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var reference = mover.GetProperty("bookingReference").GetString();
        await WithDbAsync(async db =>
        {
            var session = await db.BookingSessions.AsNoTracking().SingleAsync(s => s.BookingReference == reference);
            Assert.Equal(first, session.SelectedTime);
            Assert.Equal(0, session.RescheduleCount);

            Assert.False(await db.BookingSessionEvents
                .AnyAsync(e => e.SessionId == session.Id && e.EventType == BookingEventType.BookingRescheduled));
        });

        // And the booking that was in the way is untouched.
        Assert.NotNull(blocker.GetProperty("bookingReference").GetString());
    }
}
