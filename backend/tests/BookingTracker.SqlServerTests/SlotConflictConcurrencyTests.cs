using System.Diagnostics;
using System.Net;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.IntegrationTests.Infrastructure;
using BookingTracker.SqlServerTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace BookingTracker.SqlServerTests;

/// <summary>
/// The claim no other suite in this repository can make: that two guests
/// submitting the same slot AT THE SAME TIME produce exactly one booking.
///
/// The unit suite's BookingConflictCheckerTests and the InMemory integration
/// suite's PublicBookingLifecycleTests both assert the SEQUENTIAL contract - a
/// second booker, arriving after the first has committed, is refused. Neither
/// can say anything about a race: the InMemory provider ignores the
/// Serializable transaction SubmitBookingSessionCommandHandler opens, so there
/// is no isolation there to test.
///
/// **There is no database constraint on a booked slot.** No unique index covers
/// (organizer, date, time) - and one could not, because a conflict is an
/// *overlap* of intervals derived from each page's duration and buffers, not an
/// equality. The entire guarantee is BookingConflictChecker running inside
/// Serializable isolation, so SQL Server's locking is the mechanism, and this
/// is the only place it is exercised.
///
/// Overlap is engineered rather than hoped for: both requests are held at a
/// gate and released together, and each records the wall-clock window it
/// occupied. The test asserts those windows genuinely intersect, so a green
/// result can never mean "they happened to run one after the other".
/// </summary>
public class SlotConflictConcurrencyTests(ITestOutputHelper output) : SqlServerApiTestBase
{
    /// <summary>How many independent races each test runs. A race that fails one time in five is still broken.</summary>
    private const int Rounds = 5;

    private sealed record Attempt(HttpStatusCode Status, string Body, Guid SessionId, long StartedTicks, long FinishedTicks);

    [Fact]
    public async Task TwoGuestsSubmittingTheSameSlotAtTheSameTimeProduceExactlyOneBooking()
    {
        var wins = 0;
        var conflicts = 0;

        for (var round = 1; round <= Rounds; round++)
        {
            var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(
                db, email: Unique("race") + "@example.com", slug: Unique("race-page")));

            var date = TestData.NextBookableWeekday();
            var time = await BookingFlow.FirstAvailableTimeAsync(Client, workspace.Page.Slug, date);

            var attempts = await RaceAsync(workspace.Page.Slug, date, time);

            var winner = Assert.Single(attempts, a => a.Status == HttpStatusCode.OK);
            var loser = Assert.Single(attempts, a => a.Status != HttpStatusCode.OK);

            AssertOverlapped(attempts, round);

            // The public contract, not merely "the other one failed". A raw
            // SqlException surfacing as a 500 would satisfy "one of them
            // failed" while telling the guest nothing they can act on.
            Assert.Equal(HttpStatusCode.Conflict, loser.Status);
            Assert.Contains("no longer available", loser.Body, StringComparison.OrdinalIgnoreCase);

            await AssertExactlyOneBookingAsync(workspace, date, time, winner.SessionId, loser.SessionId, round);

            wins++;
            conflicts++;
            output.WriteLine($"round {round}: winner={winner.SessionId} ({(int)winner.Status}), loser={loser.SessionId} ({(int)loser.Status})");
        }

        output.WriteLine($"""

            Concurrent submissions: {Rounds * 2} (across {Rounds} races)
            Successful bookings:    {wins}
            Rejected bookings:      {conflicts}
            """);
    }

    [Fact]
    public async Task TwoGuestsRacingOnDifferentPagesOfTheSameOrganizerStillProduceOneBooking()
    {
        // Cross-page double-booking prevention is an organizer-wide rule
        // (BookingConflictChecker joins through BookingPages on OrganizerId),
        // so the two racing requests here do not even touch the same booking
        // page row - only the same organizer's calendar.
        var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(
            db, email: Unique("cross") + "@example.com", slug: Unique("cross-a")));

        var secondSlug = Unique("cross-b");
        await WithDbAsync(async db =>
        {
            db.BookingPages.Add(BookingPage.Create(workspace.Organizer.Id, secondSlug, "Second", 30, 0, 0));
            await db.SaveChangesAsync();
        });

        var date = TestData.NextBookableWeekday();
        var time = await BookingFlow.FirstAvailableTimeAsync(Client, workspace.Page.Slug, date);

        var attempts = await RaceAsync(workspace.Page.Slug, date, time, secondSlug);

        AssertOverlapped(attempts, round: 1);
        Assert.Single(attempts, a => a.Status == HttpStatusCode.OK);
        Assert.Single(attempts, a => a.Status == HttpStatusCode.Conflict);

        var submitted = await WithDbAsync(db => db.BookingSessions
            .Where(s => s.Status == BookingSessionStatus.Submitted && s.SelectedDate == date && s.SelectedTime == time)
            .Join(db.BookingPages, s => s.BookingPageId, p => p.Id, (s, p) => new { s.Id, p.OrganizerId })
            .Where(x => x.OrganizerId == workspace.Organizer.Id)
            .CountAsync());

        Assert.Equal(1, submitted);
    }

    /// <summary>
    /// The counterpart to every other test in this file: guests who are NOT
    /// competing must not be made to compete.
    ///
    /// Four guests, four different organizers, four different booking pages, all
    /// submitting at the same instant. Nothing here is a conflict in any sense
    /// the domain recognises, so all four bookings must be taken.
    ///
    /// This is a regression test for a real defect rather than a hypothetical:
    /// before the (BookingPageId, Status, SelectedDate) index, the conflict
    /// query could only be answered by combining two indexes, and the second
    /// half of that plan was a full index scan - which under Serializable takes
    /// a table-level S lock on BookingSessions rather than key-range locks. Every
    /// claim transaction therefore locked the entire table, so any two concurrent
    /// bookings deadlocked no matter who they belonged to. Measured at the time:
    /// one booking succeeded and every other racer got HTTP 500, in twelve rounds
    /// out of twelve.
    ///
    /// It lives here rather than in Diagnostics/ because it asserts a contract
    /// rather than reporting a number. Its value is entirely in the index it
    /// protects, so if it ever fails, look at the execution plan before looking
    /// at the test - see Diagnostics/ConflictQueryDiagnostics.
    ///
    /// <see cref="BookingVolume"/> is why it needs a populated table, and is not
    /// an attempt to make a flaky test pass: below a thousand or so bookings the
    /// optimizer scans - correctly, because at that size a scan is cheapest - and
    /// a scan under Serializable locks every row it read. Run on an empty
    /// database this measures the table's size rather than the product, and it
    /// was written that way first and failed for exactly that reason.
    /// </summary>
    [Fact]
    public async Task GuestsBookingDifferentOrganizersAtTheSameInstantAllSucceed()
    {
        const int guests = 4;

        await BookingVolume.EnsureAsync();

        var date = TestData.NextBookableWeekday();
        var clients = new List<HttpClient>();
        var racers = new List<Diagnostics.ContentionHarness.Racer>();

        try
        {
            TimeOnly? time = null;

            for (var i = 0; i < guests; i++)
            {
                var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(
                    db, email: Unique("solo") + "@example.com", slug: Unique("solo-page")));

                time ??= await BookingFlow.FirstAvailableTimeAsync(Client, workspace.Page.Slug, date);

                var client = Factory.CreateClient();
                clients.Add(client);

                var sessionId = await BookingFlow.StartSessionAsync(client, workspace.Page.Slug);
                await BookingFlow.FillAsync(client, sessionId, date, time.Value,
                    $"Guest {i}", $"guest{i}@example.com");

                racers.Add(new Diagnostics.ContentionHarness.Racer(client, sessionId));
            }

            var result = await Diagnostics.ContentionHarness.RaceAsync(racers);

            output.WriteLine(
                $"{guests} unrelated guests: {result.Ok} booked, {result.Conflicts} refused, " +
                $"{result.ServerErrors} failed, {result.Deadlocks} deadlock(s), " +
                $"max {result.Max:F0}ms");

            foreach (var unexpected in result.Unexpected) output.WriteLine("  " + unexpected);

            Assert.Equal(guests, result.Ok);
            Assert.Equal(0, result.Conflicts);
            Assert.Equal(0, result.ServerErrors);

            var booked = await WithDbAsync(db => db.BookingSessions
                .CountAsync(s => racers.Select(r => r.SessionId).Contains(s.Id)
                                 && s.Status == BookingSessionStatus.Submitted));

            Assert.Equal(guests, booked);
        }
        finally
        {
            foreach (var client in clients) client.Dispose();
        }
    }

    /// <summary>
    /// The claim-gate contract, at every contention level: one winner, everybody
    /// else a 409, and - the part that is new - <b>no deadlock at all</b>.
    ///
    /// The two-racer test above already pinned the answer. What it could not pin
    /// is how the answer is reached, and that used to be the whole cost: every
    /// loser lost by being chosen as a deadlock victim, which SQL Server decides
    /// on its monitor's ~5s cycle. Two guests measured 2.4-4.9 seconds for a
    /// result the product could have given them in milliseconds.
    ///
    /// So the assertion that matters here is <c>Deadlocks == 0</c>. It is read
    /// from SQL Server's own cumulative counter, which is instance-wide - safe
    /// because this collection runs with parallelisation off against a database
    /// nothing else uses, and worth the caveat because it is the only assertion
    /// in the suite that is not scoped to its own organizer.
    ///
    /// <see cref="BookingVolume"/> for the same reason as the unrelated-guests
    /// test below it: on a table small enough to scan, the lock footprint is the
    /// table and this measures the fixture rather than the product.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    public async Task ContestedSlotProducesOneBookingAndNoDeadlocksAtAnyContentionLevel(int racers)
    {
        await BookingVolume.EnsureAsync();

        var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(
            db, email: Unique("contested") + "@example.com", slug: Unique("contested-page")));

        var date = TestData.NextBookableWeekday();
        var time = await BookingFlow.FirstAvailableTimeAsync(Client, workspace.Page.Slug, date);

        var prepared = new List<Diagnostics.ContentionHarness.Racer>();
        try
        {
            for (var i = 0; i < racers; i++)
            {
                var client = Factory.CreateClient();
                var sessionId = await BookingFlow.StartSessionAsync(client, workspace.Page.Slug);
                await BookingFlow.FillAsync(client, sessionId, date, time, $"Guest {i}", $"guest{i}@example.com");
                prepared.Add(new Diagnostics.ContentionHarness.Racer(client, sessionId));
            }

            var result = await Diagnostics.ContentionHarness.RaceAsync(prepared);

            output.WriteLine(
                $"{racers} racers for one slot: {result.Ok} booked, {result.Conflicts} refused, " +
                $"{result.ServerErrors} failed, {result.Deadlocks} deadlock(s), " +
                $"wall {result.WallClockMs:F0}ms, p50 {result.P50:F0}ms, max {result.Max:F0}ms");

            foreach (var unexpected in result.Unexpected) output.WriteLine("  " + unexpected);

            Assert.Equal(1, result.Ok);
            Assert.Equal(racers - 1, result.Conflicts);
            Assert.Equal(0, result.ServerErrors);
            Assert.Equal(0, result.Other);

            Assert.True(result.Deadlocks == 0,
                $"The claim gate is supposed to make contested claims WAIT for each other, but SQL Server " +
                $"resolved {result.Deadlocks} of them by deadlock.");

            var winners = await WithDbAsync(db => db.BookingSessions
                .Where(s => prepared.Select(r => r.SessionId).Contains(s.Id)
                            && s.Status == BookingSessionStatus.Submitted)
                .Select(s => s.Id)
                .ToListAsync());

            var winnerId = Assert.Single(winners);

            await WithDbAsync(async db =>
            {
                var losers = prepared.Select(r => r.SessionId).Where(id => id != winnerId).ToList();

                Assert.Empty(await db.BookingSessionEvents
                    .Where(e => e.EventType == BookingEventType.BookingSubmitted && losers.Contains(e.SessionId))
                    .ToListAsync());
                Assert.Empty(await db.EmailNotifications.Where(n => losers.Contains(n.BookingSessionId!.Value)).ToListAsync());
                Assert.Empty(await db.BookingReminders.Where(r => losers.Contains(r.BookingSessionId)).ToListAsync());

                Assert.Equal(2, await db.EmailNotifications.CountAsync(n => n.BookingSessionId == winnerId));
                Assert.Equal(1, await db.BookingReminders.CountAsync(r => r.BookingSessionId == winnerId));
            });

            // The losers never committed, so no best-effort block ran for them -
            // including the calendar one, which is the only side effect that
            // would have left this application entirely.
            Assert.Equal([winnerId], Factory.Calendar.Created.Intersect(prepared.Select(r => r.SessionId)).ToList());
        }
        finally
        {
            foreach (var racer in prepared) racer.Client.Dispose();
        }
    }

    /// <summary>
    /// Guests taking DIFFERENT slots on the SAME organizer must all be booked.
    ///
    /// This is the case the claim gate could plausibly have broken, and the
    /// reason it is worth a test of its own: the gate serializes claims per
    /// organizer, so these four genuinely do queue behind one another where
    /// before they did not. Queueing is acceptable - it is a few milliseconds
    /// each and it is what makes the organizer-wide overlap rule checkable at
    /// all - but losing a booking is not, and neither is a deadlock.
    ///
    /// Slots are a whole duration apart rather than adjacent, because this page
    /// offers slots every 15 minutes for 30-minute appointments: consecutive
    /// slots overlap, and four of those would be a genuine conflict rather than
    /// the independent bookings this test is about.
    /// </summary>
    [Fact]
    public async Task GuestsTakingDifferentSlotsOnOneOrganizerAreAllBooked()
    {
        const int guests = 4;

        await BookingVolume.EnsureAsync();

        var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(
            db, email: Unique("spread") + "@example.com", slug: Unique("spread-page")));

        var date = TestData.NextBookableWeekday();
        var slots = await BookingFlow.GetSlotsAsync(Client, workspace.Page.Slug, date, date);
        var first = TimeOnly.Parse(slots[0].GetProperty("localStartTime").GetString()!);

        var prepared = new List<Diagnostics.ContentionHarness.Racer>();
        try
        {
            for (var i = 0; i < guests; i++)
            {
                var time = first.AddMinutes(workspace.Page.DurationMinutes * i);
                var client = Factory.CreateClient();
                var sessionId = await BookingFlow.StartSessionAsync(client, workspace.Page.Slug);
                await BookingFlow.FillAsync(client, sessionId, date, time, $"Guest {i}", $"guest{i}@example.com");
                prepared.Add(new Diagnostics.ContentionHarness.Racer(client, sessionId));
            }

            var result = await Diagnostics.ContentionHarness.RaceAsync(prepared);

            output.WriteLine(
                $"{guests} guests, {guests} different slots, one organizer: {result.Ok} booked, " +
                $"{result.Conflicts} refused, {result.ServerErrors} failed, {result.Deadlocks} deadlock(s), " +
                $"max {result.Max:F0}ms");

            foreach (var unexpected in result.Unexpected) output.WriteLine("  " + unexpected);

            Assert.Equal(guests, result.Ok);
            Assert.Equal(0, result.Conflicts);
            Assert.Equal(0, result.ServerErrors);
            Assert.Equal(0, result.Deadlocks);
        }
        finally
        {
            foreach (var racer in prepared) racer.Client.Dispose();
        }
    }

    /// <summary>
    /// A reschedule and a submit racing for one slot, because
    /// <c>ClaimSlotAsync</c> is shared by both and the gate therefore has to
    /// hold for the pair, not only for two submits.
    ///
    /// Whichever of them gets there first, the other must be told the slot is
    /// gone - and the reschedule losing has to leave a LIVE booking exactly
    /// where it was, which is the more expensive failure of the two.
    /// </summary>
    [Fact]
    public async Task ARescheduleAndASubmitRacingForOneSlotProduceOneBooking()
    {
        await BookingVolume.EnsureAsync();

        var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(
            db, email: Unique("mixed") + "@example.com", slug: Unique("mixed-page")));

        var date = TestData.NextBookableWeekday();
        var slots = await BookingFlow.GetSlotsAsync(Client, workspace.Page.Slug, date, date);
        var origin = TimeOnly.Parse(slots[0].GetProperty("localStartTime").GetString()!);
        var contested = origin.AddMinutes(workspace.Page.DurationMinutes);

        // The booking that will try to move into the contested slot.
        var mover = await BookingFlow.BookAsync(Client, workspace.Page.Slug, date, origin, "Mover", "mover@example.com");
        var moverToken = mover.GetProperty("publicToken").GetString();
        var moverReference = mover.GetProperty("bookingReference").GetString();

        using var submitter = Factory.CreateClient();
        var newcomerId = await BookingFlow.StartSessionAsync(submitter, workspace.Page.Slug);
        await BookingFlow.FillAsync(submitter, newcomerId, date, contested, "Newcomer", "newcomer@example.com");

        using var rescheduler = Factory.CreateClient();
        var body = $$"""{"newDate":"{{date:yyyy-MM-dd}}","newTime":"{{contested:HH\:mm\:ss}}"}""";

        var deadlocksBefore = await Diagnostics.ContentionHarness.DeadlockCountAsync();

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new[]
        {
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
        };

        var submit = ReleaseThenAsync(ready[0], gate,
            () => BookingFlow.SubmitAsync(submitter, newcomerId));
        var reschedule = ReleaseThenAsync(ready[1], gate,
            () => rescheduler.PostAsync($"/api/bookings/{moverToken}/reschedule", RawJson(body)));

        await Task.WhenAll(ready.Select(r => r.Task));
        gate.SetResult();

        var responses = await Task.WhenAll(submit, reschedule);
        var deadlocks = await Diagnostics.ContentionHarness.DeadlockCountAsync() - deadlocksBefore;

        output.WriteLine(
            $"submit={(int)responses[0].StatusCode}, reschedule={(int)responses[1].StatusCode}, " +
            $"deadlocks={deadlocks}");

        Assert.Equal(0, deadlocks);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);

        await WithDbAsync(async db =>
        {
            var onContested = await db.BookingSessions
                .Where(s => s.BookingPageId == workspace.Page.Id
                            && s.Status == BookingSessionStatus.Submitted
                            && s.SelectedDate == date
                            && s.SelectedTime == contested)
                .CountAsync();

            Assert.Equal(1, onContested);

            // Whoever lost, the mover is still a live booking on exactly one of
            // the two slots - never half-moved, and never gone.
            var moverSession = await db.BookingSessions.AsNoTracking()
                .SingleAsync(s => s.BookingReference == moverReference);

            Assert.Equal(BookingSessionStatus.Submitted, moverSession.Status);
            Assert.Contains(moverSession.SelectedTime, new TimeOnly?[] { origin, contested });

            if (moverSession.SelectedTime == origin)
            {
                Assert.Equal(0, moverSession.RescheduleCount);
                Assert.False(await db.BookingSessionEvents.AnyAsync(
                    e => e.SessionId == moverSession.Id && e.EventType == BookingEventType.BookingRescheduled));
            }
        });

        foreach (var response in responses) response.Dispose();
    }

    private static async Task<HttpResponseMessage> ReleaseThenAsync(
        TaskCompletionSource ready, TaskCompletionSource gate, Func<Task<HttpResponseMessage>> call)
    {
        await Task.Yield();
        ready.SetResult();
        await gate.Task;
        return await call();
    }

    // ---- the race itself ----------------------------------------------------

    /// <summary>
    /// Two independent clients, two independent sessions, both filled in and
    /// held at a gate, then released together.
    ///
    /// A TaskCompletionSource with RunContinuationsAsynchronously is what makes
    /// this a real race rather than a sequence: both awaiting tasks are
    /// scheduled onto the pool by the single SetResult, so neither is resumed
    /// inline by the releasing thread. There is deliberately no Task.Delay
    /// anywhere - a manufactured pause would prove nothing about timing that
    /// the recorded windows do not prove properly.
    /// </summary>
    private async Task<Attempt[]> RaceAsync(string slugA, DateOnly date, TimeOnly time, string? slugB = null)
    {
        using var clientA = Factory.CreateClient();
        using var clientB = Factory.CreateClient();

        var sessionA = await PrepareAsync(clientA, slugA, date, time, "Ada Lovelace", "ada@example.com");
        var sessionB = await PrepareAsync(clientB, slugB ?? slugA, date, time, "Grace Hopper", "grace@example.com");

        var readyA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var taskA = SubmitWhenReleasedAsync(clientA, sessionA, readyA, gate);
        var taskB = SubmitWhenReleasedAsync(clientB, sessionB, readyB, gate);

        await Task.WhenAll(readyA.Task, readyB.Task);
        gate.SetResult();

        return await Task.WhenAll(taskA, taskB);
    }

    private static async Task<Guid> PrepareAsync(
        HttpClient client, string slug, DateOnly date, TimeOnly time, string name, string email)
    {
        var sessionId = await BookingFlow.StartSessionAsync(client, slug);
        await BookingFlow.FillAsync(client, sessionId, date, time, name, email);
        return sessionId;
    }

    private static async Task<Attempt> SubmitWhenReleasedAsync(
        HttpClient client, Guid sessionId, TaskCompletionSource ready, TaskCompletionSource gate)
    {
        await Task.Yield();
        ready.SetResult();
        await gate.Task;

        var startedTicks = Stopwatch.GetTimestamp();
        var response = await BookingFlow.SubmitAsync(client, sessionId);
        var body = await response.Content.ReadAsStringAsync();
        var finishedTicks = Stopwatch.GetTimestamp();

        return new Attempt(response.StatusCode, body, sessionId, startedTicks, finishedTicks);
    }

    // ---- assertions ---------------------------------------------------------

    /// <summary>
    /// The half of a concurrency test that is usually left out: proof that the
    /// two requests were actually in flight together. Without it, a 200 and a
    /// 409 are equally consistent with the two having run one after the other,
    /// which every other suite here already covers.
    /// </summary>
    private void AssertOverlapped(Attempt[] attempts, int round)
    {
        var (a, b) = (attempts[0], attempts[1]);
        var overlapped = a.StartedTicks < b.FinishedTicks && b.StartedTicks < a.FinishedTicks;

        var origin = Math.Min(a.StartedTicks, b.StartedTicks);
        double Ms(long ticks) => (ticks - origin) * 1000.0 / Stopwatch.Frequency;

        var window =
            $"A [{Ms(a.StartedTicks):F1} .. {Ms(a.FinishedTicks):F1}] ms, " +
            $"B [{Ms(b.StartedTicks):F1} .. {Ms(b.FinishedTicks):F1}] ms";

        output.WriteLine($"round {round}: {window}, overlapped={overlapped}");

        Assert.True(overlapped,
            $"Round {round}: the two submissions did not overlap in time, so this proves nothing " +
            $"about concurrency. {window}");
    }

    /// <summary>
    /// One booking, one event, and side effects for the winner only - the loser
    /// must leave nothing behind at all.
    /// </summary>
    private async Task AssertExactlyOneBookingAsync(
        TestData.Workspace workspace, DateOnly date, TimeOnly time, Guid winnerId, Guid loserId, int round)
    {
        await WithDbAsync(async db =>
        {
            var submitted = await db.BookingSessions
                .Where(s => s.BookingPageId == workspace.Page.Id
                            && s.Status == BookingSessionStatus.Submitted
                            && s.SelectedDate == date
                            && s.SelectedTime == time)
                .Select(s => s.Id)
                .ToListAsync();

            Assert.Equal(new[] { winnerId }, submitted);

            // The losing session is untouched, not half-submitted: still Active,
            // and with none of the identity a confirmed booking carries.
            var loser = await db.BookingSessions.AsNoTracking().SingleAsync(s => s.Id == loserId);
            Assert.Equal(BookingSessionStatus.Active, loser.Status);
            Assert.Null(loser.BookingReference);
            Assert.Null(loser.PublicToken);

            var submittedEvents = await db.BookingSessionEvents
                .Where(e => e.EventType == BookingEventType.BookingSubmitted
                            && (e.SessionId == winnerId || e.SessionId == loserId))
                .Select(e => e.SessionId)
                .ToListAsync();

            Assert.Equal(new[] { winnerId }, submittedEvents);

            // One booking, one set of side effects. The loser queues no email
            // and schedules no reminder, because its transaction never
            // committed anything for the best-effort blocks to run against.
            var emails = await db.EmailNotifications
                .Where(n => n.BookingSessionId == winnerId || n.BookingSessionId == loserId)
                .GroupBy(n => n.BookingSessionId)
                .Select(g => new { SessionId = g.Key, Count = g.Count() })
                .ToListAsync();

            var reminders = await db.BookingReminders
                .Where(r => r.BookingSessionId == winnerId || r.BookingSessionId == loserId)
                .GroupBy(r => r.BookingSessionId)
                .Select(g => new { SessionId = g.Key, Count = g.Count() })
                .ToListAsync();

            Assert.DoesNotContain(emails, e => e.SessionId == loserId);
            Assert.DoesNotContain(reminders, r => r.SessionId == loserId);

            var winnerEmails = emails.SingleOrDefault(e => e.SessionId == winnerId)?.Count ?? 0;
            var winnerReminders = reminders.SingleOrDefault(r => r.SessionId == winnerId)?.Count ?? 0;

            output.WriteLine(
                $"round {round}: rows persisted=1, emails queued (winner)={winnerEmails}, " +
                $"reminders created (winner)={winnerReminders}, loser side effects=0");

            // Guest confirmation + organizer notice, from the default settings
            // an organizer with no NotificationSettings row falls back to.
            Assert.Equal(2, winnerEmails);
            Assert.Equal(1, winnerReminders);
        });
    }
}
