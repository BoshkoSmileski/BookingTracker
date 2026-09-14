using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.Domain.ValueObjects;
using BookingTracker.Infrastructure.BackgroundServices;
using BookingTracker.Infrastructure.Persistence;
using BookingTracker.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookingTracker.UnitTests.Infrastructure.BackgroundServices;

/// <summary>
/// Which sessions the abandonment sweep is allowed to touch.
///
/// BookingSession.Abandon() is covered at the entity level in BookingSessionTests;
/// what had never been executed by any test is the sweeper's own selection query -
/// the Status == Active AND LastActivityAt &lt; cutoff pair that decides who it is
/// applied to. This is the worker's eligibility contract, and a widening of it
/// would rewrite bookings nobody asked it to look at.
///
/// The sweep is global across organizers by design: abandonment is a property of
/// one session's own inactivity, with no per-organizer configuration to respect,
/// so every selected row is independently valid. That is asserted rather than
/// assumed below.
/// </summary>
public class BookingSessionAbandonmentSweeperTests
{
    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(5);

    private static BookingTrackerDbContext Context(string databaseName) =>
        new(new DbContextOptionsBuilder<BookingTrackerDbContext>()
            .UseInMemoryDatabase(databaseName).EnableSensitiveDataLogging().Options);

    /// <summary>
    /// LastActivityAt has a private setter and is only ever moved by Apply(), so a
    /// test that wants a session idle for six minutes writes it through the change
    /// tracker - the same approach EmailQueueProcessorRetryTests takes to
    /// NextAttemptAtUtc, and for the same reason: the alternative is sleeping.
    /// </summary>
    private static void SetLastActivity(BookingTrackerDbContext db, BookingSession session, DateTime whenUtc)
    {
        db.Entry(session).Property(nameof(BookingSession.LastActivityAt)).CurrentValue = whenUtc;
        db.SaveChanges();
    }

    private static BookingSession SeedSession(BookingTrackerDbContext db, Guid pageId, DateTime lastActivityUtc)
    {
        var (session, startedEvent) = BookingSession.Start(pageId, ClientContext.Unknown);
        db.BookingSessions.Add(session);
        db.BookingSessionEvents.Add(startedEvent);
        db.SaveChanges();
        SetLastActivity(db, session, lastActivityUtc);
        return session;
    }

    private static async Task SweepAsync(BookingTrackerDbContext db)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBookingTrackerDbContext>(db);
        services.AddSingleton<IEventBroadcaster>(new FakeEventBroadcaster());
        using var provider = services.BuildServiceProvider();

        var sweeper = new BookingSessionAbandonmentSweeper(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BookingSessionAbandonmentSweeper>.Instance);

        // SweepAsync is private; the hosted loop runs one sweep and then parks on its
        // 30-second delay, so cancelling afterwards ends it.
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(2));
        try { await sweeper.StartAsync(cts.Token); } catch (OperationCanceledException) { }
        try { if (sweeper.ExecuteTask is { } running) await running; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task ASessionIdleBeyondTheThreshold_IsAbandonedAndSaysSoInItsOwnEventLog()
    {
        var database = Guid.NewGuid().ToString();
        Guid sessionId;

        using (var db = Context(database))
        {
            var organizer = TestEntities.CreateOrganizer();
            var page = TestEntities.CreateBookingPage(organizer.Id);
            db.Organizers.Add(organizer);
            db.BookingPages.Add(page);
            db.SaveChanges();
            sessionId = SeedSession(db, page.Id, DateTime.UtcNow - Threshold - TimeSpan.FromMinutes(1)).Id;

            await SweepAsync(db);
        }

        using var check = Context(database);
        var session = await check.BookingSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        Assert.Equal(BookingSessionStatus.Abandoned, session.Status);

        // The projection must never hold something the log cannot derive.
        var abandonedEvents = await check.BookingSessionEvents.AsNoTracking()
            .CountAsync(e => e.SessionId == sessionId && e.EventType == BookingEventType.BookingAbandoned);
        Assert.Equal(1, abandonedEvents);
    }

    [Fact]
    public async Task ASessionStillInsideTheThreshold_IsLeftAlone()
    {
        // The boundary that matters: a guest who paused for four minutes is still
        // filling the form in, and abandoning them would be visible on the organizer's
        // live dashboard as a booking that gave up while it was being typed.
        var database = Guid.NewGuid().ToString();
        Guid sessionId;

        using (var db = Context(database))
        {
            var organizer = TestEntities.CreateOrganizer();
            var page = TestEntities.CreateBookingPage(organizer.Id);
            db.Organizers.Add(organizer);
            db.BookingPages.Add(page);
            db.SaveChanges();
            sessionId = SeedSession(db, page.Id, DateTime.UtcNow - TimeSpan.FromMinutes(4)).Id;

            await SweepAsync(db);
        }

        using var check = Context(database);
        var session = await check.BookingSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        Assert.Equal(BookingSessionStatus.Active, session.Status);
        Assert.Empty(await check.BookingSessionEvents.AsNoTracking()
            .Where(e => e.SessionId == sessionId && e.EventType == BookingEventType.BookingAbandoned)
            .ToListAsync());
    }

    [Fact]
    public async Task ASubmittedBooking_IsNeverAbandonedNoMatterHowLongItHasBeenIdle()
    {
        // A confirmed booking is idle by definition - nobody is typing into it - so
        // an eligibility rule resting on inactivity alone would abandon every booking
        // in the system five minutes after it was made.
        var database = Guid.NewGuid().ToString();
        Guid sessionId;

        using (var db = Context(database))
        {
            var organizer = TestEntities.CreateOrganizer();
            var page = TestEntities.CreateBookingPage(organizer.Id);
            db.Organizers.Add(organizer);
            db.BookingPages.Add(page);

            var booked = BookingSessionScenarios.StartFillAndSubmit(page.Id);
            db.BookingSessions.Add(booked.Session);
            db.BookingSessionEvents.AddRange(booked.Events);
            db.SaveChanges();
            SetLastActivity(db, booked.Session, DateTime.UtcNow - TimeSpan.FromDays(30));
            sessionId = booked.Session.Id;

            await SweepAsync(db);
        }

        using var check = Context(database);
        var session = await check.BookingSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        Assert.Equal(BookingSessionStatus.Submitted, session.Status);
        Assert.NotNull(session.BookingReference);
    }

    [Fact]
    public async Task RepeatedSweeps_AbandonASessionOnceAndAppendNothingFurther()
    {
        // The sweep runs every 30 seconds forever, so "already abandoned" is by far
        // its most common encounter with any given row.
        var database = Guid.NewGuid().ToString();
        Guid sessionId;

        using (var db = Context(database))
        {
            var organizer = TestEntities.CreateOrganizer();
            var page = TestEntities.CreateBookingPage(organizer.Id);
            db.Organizers.Add(organizer);
            db.BookingPages.Add(page);
            db.SaveChanges();
            sessionId = SeedSession(db, page.Id, DateTime.UtcNow - Threshold - TimeSpan.FromMinutes(1)).Id;

            await SweepAsync(db);
            await SweepAsync(db);
        }

        using (var restarted = Context(database))
        {
            await SweepAsync(restarted);
        }

        using var check = Context(database);
        Assert.Equal(1, await check.BookingSessionEvents.AsNoTracking()
            .CountAsync(e => e.SessionId == sessionId && e.EventType == BookingEventType.BookingAbandoned));
    }

    [Fact]
    public async Task TheSweepIsGlobal_AndEachOrganizersStaleSessionIsAbandonedOnItsOwnMerits()
    {
        // Deliberately global: there is no per-organizer abandonment setting to
        // respect, so processing every tenant is correct rather than a leak. What
        // must hold is that eligibility is still decided per row - organizer B's
        // fresh session is not swept up alongside organizer A's stale one.
        var database = Guid.NewGuid().ToString();
        Guid staleForA, freshForB;

        using (var db = Context(database))
        {
            var organizerA = TestEntities.CreateOrganizer(email: "a@example.com");
            var organizerB = TestEntities.CreateOrganizer(email: "b@example.com");
            var pageA = TestEntities.CreateBookingPage(organizerA.Id, slug: "page-a");
            var pageB = TestEntities.CreateBookingPage(organizerB.Id, slug: "page-b");
            db.Organizers.AddRange(organizerA, organizerB);
            db.BookingPages.AddRange(pageA, pageB);
            db.SaveChanges();

            staleForA = SeedSession(db, pageA.Id, DateTime.UtcNow - Threshold - TimeSpan.FromMinutes(1)).Id;
            freshForB = SeedSession(db, pageB.Id, DateTime.UtcNow - TimeSpan.FromMinutes(1)).Id;

            await SweepAsync(db);
        }

        using var check = Context(database);
        Assert.Equal(BookingSessionStatus.Abandoned, (await check.BookingSessions.AsNoTracking().SingleAsync(s => s.Id == staleForA)).Status);
        Assert.Equal(BookingSessionStatus.Active, (await check.BookingSessions.AsNoTracking().SingleAsync(s => s.Id == freshForB)).Status);
    }
}
