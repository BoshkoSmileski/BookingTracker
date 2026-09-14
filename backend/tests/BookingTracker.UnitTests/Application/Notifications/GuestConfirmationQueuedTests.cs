using BookingTracker.Application.Bookings.Commands.CancelBooking;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Notifications;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BookingTracker.UnitTests.Application.Notifications;

/// <summary>
/// Whether the guest was told anything - reported honestly, all the way from
/// the queue to the DTO a guest screen reads.
///
/// The distinction this pins is queued vs sent. Nothing has been transmitted
/// when a lifecycle handler returns: EmailQueueProcessor drains the row later,
/// out of the request, and may retry or ultimately fail. So the strongest true
/// claim is "an EmailNotification row exists", and the screens say "on its way"
/// only on the strength of that. They used to say it on the strength of the
/// booking carrying an email address, which is a different fact entirely - an
/// organizer with NotifyGuestOnBooking switched off produces no email at all,
/// and the guest was promised one anyway.
/// </summary>
public class GuestConfirmationQueuedTests
{
    private static EmailNotificationService CreateService(BookingTracker.Infrastructure.Persistence.BookingTrackerDbContext db) =>
        new(db, new EmailTemplateRenderer(), new CalendarInvitationGenerator(), new FakeFrontendLinkBuilder(),
            Options.Create(new EmailNotificationSettings()), NullLogger<EmailNotificationService>.Instance);

    private static async Task<(Organizer Organizer, BookingPage Page, BookingSessionScenarios.Result Seeded)> SeedAsync(
        BookingTracker.Infrastructure.Persistence.BookingTrackerDbContext db,
        bool notifyGuest = true,
        bool notifyOrganizer = true,
        bool cancelled = false)
    {
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);

        var seeded = cancelled
            ? BookingSessionScenarios.SubmitThenCancel(page.Id)
            : BookingSessionScenarios.StartFillAndSubmit(page.Id);
        db.BookingSessions.Add(seeded.Session);
        db.BookingSessionEvents.AddRange(seeded.Events);

        var settings = NotificationSettings.CreateDefault(organizer.Id);
        settings.UpdateSettings(notifyGuest, notifyOrganizer, remindersEnabled: true, reminderMinutesBeforeEvent: [1440]);
        db.NotificationSettings.Add(settings);

        await db.SaveChangesAsync();
        return (organizer, page, seeded);
    }

    // ---------- the service's own answer ----------

    [Fact]
    public async Task Confirmed_WithGuestNotificationsOn_ReportsQueued()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (_, _, seeded) = await SeedAsync(db);

        var queued = await CreateService(db).QueueBookingConfirmedAsync(seeded.Session);

        Assert.True(queued);
        Assert.Contains(await db.EmailNotifications.ToListAsync(), n => n.NotificationType == EmailNotificationType.BookingConfirmation);
    }

    [Fact]
    public async Task Confirmed_WithGuestNotificationsOff_ReportsNotQueued_EvenThoughTheOrganizerWasStillNotified()
    {
        // The case the flag exists for. Something WAS queued - just not to the
        // guest - so a naive "did anything get queued" check would have kept
        // promising the guest an email nobody wrote.
        await using var db = InMemoryDbContextFactory.Create();
        var (_, _, seeded) = await SeedAsync(db, notifyGuest: false, notifyOrganizer: true);

        var queued = await CreateService(db).QueueBookingConfirmedAsync(seeded.Session);

        Assert.False(queued);
        Assert.Single(await db.EmailNotifications.ToListAsync());
    }

    [Fact]
    public async Task Cancelled_And_Rescheduled_ReportTheGuestsCopyTheSameWay()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (_, _, seeded) = await SeedAsync(db, cancelled: true);
        var service = CreateService(db);

        Assert.True(await service.QueueBookingCancelledAsync(seeded.Session));
        Assert.True(await service.QueueBookingRescheduledAsync(seeded.Session, new DateOnly(2026, 8, 1), new TimeOnly(9, 0)));

        await using var quiet = InMemoryDbContextFactory.Create();
        var (_, _, quietSeeded) = await SeedAsync(quiet, notifyGuest: false, cancelled: true);
        var quietService = CreateService(quiet);

        Assert.False(await quietService.QueueBookingCancelledAsync(quietSeeded.Session));
        Assert.False(await quietService.QueueBookingRescheduledAsync(quietSeeded.Session, new DateOnly(2026, 8, 1), new TimeOnly(9, 0)));
    }

    [Fact]
    public async Task ABookingThatIsNotInASendableState_ReportsNotQueued()
    {
        // No name, email, token or reference - LoadInfoAsync refuses it. Nothing
        // is queued, and nothing may be claimed.
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        var (session, startEvent) = BookingSession.Start(page.Id, BookingSessionScenarios.SampleContext);
        db.BookingSessions.Add(session);
        db.BookingSessionEvents.Add(startEvent);
        await db.SaveChangesAsync();

        Assert.False(await CreateService(db).QueueBookingConfirmedAsync(session));
        Assert.Empty(await db.EmailNotifications.ToListAsync());
    }

    // ---------- and the answer reaching the DTO ----------

    [Fact]
    public async Task CancelHandler_CarriesTheQueueStateOntoTheResponse()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (_, _, seeded) = await SeedAsync(db);

        var result = await BuildHandler(db, CreateService(db))
            .Handle(Command(seeded.Session.Id), CancellationToken.None);

        Assert.True(result.GuestConfirmationQueued);
    }

    [Fact]
    public async Task CancelHandler_WithGuestNotificationsOff_SaysSoOnTheResponse()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (_, _, seeded) = await SeedAsync(db, notifyGuest: false);

        var result = await BuildHandler(db, CreateService(db))
            .Handle(Command(seeded.Session.Id), CancellationToken.None);

        Assert.False(result.GuestConfirmationQueued);
        Assert.Equal(nameof(BookingSessionStatus.Cancelled), result.Status);
    }

    [Fact]
    public async Task CancelHandler_WhenQueueingThrows_StillCancelsTheBooking_AndClaimsNothing()
    {
        // Queueing is best-effort and must never fail the cancellation. What it
        // must also never do is leave the response saying an email is on its way
        // when the call that would have written one threw.
        await using var db = InMemoryDbContextFactory.Create();
        var (_, _, seeded) = await SeedAsync(db);

        var result = await BuildHandler(db, new ThrowingEmailNotificationService())
            .Handle(Command(seeded.Session.Id), CancellationToken.None);

        Assert.Equal(nameof(BookingSessionStatus.Cancelled), result.Status);
        Assert.False(result.GuestConfirmationQueued);
        Assert.Empty(await db.EmailNotifications.ToListAsync());
    }

    private static CancelBookingCommand Command(Guid sessionId) =>
        new(sessionId, null, null, CancelledByType.Customer, "Change of plans", null, null);

    private static CancelBookingCommandHandler BuildHandler(
        BookingTracker.Infrastructure.Persistence.BookingTrackerDbContext db, IEmailNotificationService emails) =>
        new(db, new FakeEventBroadcaster(), emails, new FakeBookingReminderScheduler(),
            new FakeCalendarSyncService(), NullLogger<CancelBookingCommandHandler>.Instance);

    /// <summary>
    /// Stands in for a queue that is down - a DbContext failure, a template
    /// blowing up. Hand-written rather than mocked, like every other double in
    /// this suite.
    /// </summary>
    private sealed class ThrowingEmailNotificationService : IEmailNotificationService
    {
        public Task<bool> QueueBookingConfirmedAsync(BookingSession session, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("queue unavailable");

        public Task<bool> QueueBookingCancelledAsync(BookingSession session, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("queue unavailable");

        public Task<bool> QueueBookingRescheduledAsync(
            BookingSession session, DateOnly? previousDate, TimeOnly? previousTime, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("queue unavailable");

        public Task<Guid?> QueueReminderAsync(
            BookingSession session, string windowKey, string windowLabel, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("queue unavailable");

        public Task QueueResendBookingConfirmationAsync(BookingSession session, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("queue unavailable");
    }
}
