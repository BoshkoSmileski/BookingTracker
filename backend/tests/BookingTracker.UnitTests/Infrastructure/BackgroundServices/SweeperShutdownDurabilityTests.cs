using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Notifications;
using BookingTracker.Application.Notifications.Templates;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.Infrastructure.BackgroundServices;
using BookingTracker.Infrastructure.Persistence;
using BookingTracker.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BookingTracker.UnitTests.Infrastructure.BackgroundServices;

/// <summary>
/// What a background worker owes an external side effect it has already caused,
/// when the host shuts down in the middle of a batch.
///
/// Both workers here hand something irreversible to the outside world (an SMTP
/// message; a committed EmailNotification row that WILL become one) and then
/// record that they did. Thase 8N found the record being written once per batch,
/// with the sweep's own cancellation token - so a shutdown mid-drain discarded
/// the record of sends that had genuinely happened, and the next start repeated
/// them. Measured before the fix: three queued confirmations produced six
/// deliveries, and one BookingReminder row produced two guest emails.
///
/// Nothing here is a concurrency claim, so InMemory is the right provider
///: what is being asserted is the ORDER in which one
/// sequential sweep commits relative to its own side effects, which is a
/// property of the code rather than of the store's isolation.
///
/// A restart is modelled as a genuinely new DbContext over the same database.
/// Reusing one context would let EF's identity map answer the second sweep from
/// memory and hide exactly the state being measured.
/// </summary>
public class SweeperShutdownDurabilityTests
{
    private static BookingTrackerDbContext Context(string databaseName) =>
        new(new DbContextOptionsBuilder<BookingTrackerDbContext>()
            .UseInMemoryDatabase(databaseName).EnableSensitiveDataLogging().Options);

    private static ServiceProvider ScopeFor(BookingTrackerDbContext db, object dependency)
    {
        // Registered as instances: the container disposes what it creates, so a factory
        // registration would dispose the test's own DbContext when the sweep's scope closed.
        var services = new ServiceCollection();
        services.AddSingleton<IBookingTrackerDbContext>(db);
        if (dependency is IEmailSender sender) services.AddSingleton(sender);
        if (dependency is IEmailNotificationService notifications) services.AddSingleton(notifications);
        return services.BuildServiceProvider();
    }

    // ---- The email queue -----------------------------------------------------

    /// <summary>An IEmailSender that trips the host's shutdown token after a chosen send.</summary>
    private sealed class ShutsDownAfter : IEmailSender
    {
        private readonly CancellationTokenSource _cts;
        private readonly int _after;
        public int SendCount { get; private set; }
        public List<string> Recipients { get; } = [];

        public ShutsDownAfter(CancellationTokenSource cts, int after) { _cts = cts; _after = after; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            SendCount++;
            Recipients.Add(message.ToEmail);
            if (SendCount == _after) _cts.Cancel();
            return Task.CompletedTask;
        }
    }

    private static EmailNotification QueueConfirmation(BookingTrackerDbContext db, string toEmail)
    {
        var notification = EmailNotification.Create(
            null, EmailNotificationType.BookingConfirmation, toEmail, "Jane Doe",
            "Your booking is confirmed", "<p>html</p>", "text", "BookingConfirmation", 5);
        db.EmailNotifications.Add(notification);
        db.SaveChanges();
        return notification;
    }

    private static async Task SweepEmailQueueAsync(
        BookingTrackerDbContext db, IEmailSender sender, CancellationToken cancellationToken)
    {
        using var provider = ScopeFor(db, sender);
        var processor = new EmailQueueProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new EmailNotificationSettings()),
            NullLogger<EmailQueueProcessor>.Instance);

        await processor.ProcessDueNotificationsAsync(cancellationToken);
    }

    [Fact]
    public async Task AnEmailAlreadyHandedToTheTransport_IsRecordedSentEvenWhileShuttingDown()
    {
        // The core of it: SendAsync cannot be taken back, so the row that says it
        // happened must not be abandoned because the host is stopping.
        var database = Guid.NewGuid().ToString();
        using var db = Context(database);
        QueueConfirmation(db, "a@example.com");
        QueueConfirmation(db, "b@example.com");

        using var cts = new CancellationTokenSource();
        var sender = new ShutsDownAfter(cts, after: 1);

        await SweepEmailQueueAsync(db, sender, cts.Token);

        db.ChangeTracker.Clear();
        var sent = await db.EmailNotifications.AsNoTracking().SingleAsync(n => n.ToEmail == "a@example.com");
        Assert.Equal(EmailNotificationStatus.Sent, sent.Status);
        Assert.NotNull(sent.SentAtUtc);
    }

    [Fact]
    public async Task AShutdownStopsTheBatch_InsteadOfSendingTheRestOfIt()
    {
        // Cancellation used to be observed only at the batch's closing SaveChanges,
        // so a stopping host still worked its way through every remaining message.
        var database = Guid.NewGuid().ToString();
        using var db = Context(database);
        QueueConfirmation(db, "a@example.com");
        QueueConfirmation(db, "b@example.com");
        QueueConfirmation(db, "c@example.com");

        using var cts = new CancellationTokenSource();
        var sender = new ShutsDownAfter(cts, after: 1);

        await SweepEmailQueueAsync(db, sender, cts.Token);

        Assert.Equal(1, sender.SendCount);

        db.ChangeTracker.Clear();
        var stillPending = await db.EmailNotifications.AsNoTracking()
            .CountAsync(n => n.Status == EmailNotificationStatus.Pending);
        Assert.Equal(2, stillPending);
    }

    [Fact]
    public async Task AnInterruptedSweepAndTheRestartAfterIt_DeliverEachEmailExactlyOnce()
    {
        // REGRESSION GUARD. Before the fix this was three queued and six delivered -
        // every guest received the confirmation twice, for the ordinary reason that
        // the app was restarted while the queue had work in it.
        var database = Guid.NewGuid().ToString();

        using (var db = Context(database))
        {
            QueueConfirmation(db, "a@example.com");
            QueueConfirmation(db, "b@example.com");
            QueueConfirmation(db, "c@example.com");
        }

        var interrupted = new ShutsDownAfter(new CancellationTokenSource(), after: 1);
        using (var db = Context(database))
        using (var cts = new CancellationTokenSource())
        {
            var sender = new ShutsDownAfter(cts, after: 1);
            await SweepEmailQueueAsync(db, sender, cts.Token);
            interrupted = sender;
        }

        var afterRestart = new ShutsDownAfter(new CancellationTokenSource(), after: -1);
        using (var db = Context(database))
        {
            await SweepEmailQueueAsync(db, afterRestart, CancellationToken.None);
        }

        var everyDelivery = interrupted.Recipients.Concat(afterRestart.Recipients).ToList();
        Assert.Equal(3, everyDelivery.Count);
        Assert.Equal(3, everyDelivery.Distinct().Count());
    }

    // ---- The reminder sweep --------------------------------------------------

    private static IEmailNotificationService RealNotificationService(BookingTrackerDbContext db) =>
        new EmailNotificationService(
            db, new EmailTemplateRenderer(), new CalendarInvitationGenerator(), new FakeFrontendLinkBuilder(),
            Options.Create(new EmailNotificationSettings()), NullLogger<EmailNotificationService>.Instance);

    /// <summary>
    /// The real notification service, with the host stopping the instant the
    /// notification row has been committed - the exact window between
    /// QueueReminderAsync's own SaveChanges and the reminder's MarkQueued.
    /// </summary>
    private sealed class ShutsDownOnceQueued : IEmailNotificationService
    {
        private readonly IEmailNotificationService _inner;
        private readonly CancellationTokenSource _cts;
        public ShutsDownOnceQueued(IEmailNotificationService inner, CancellationTokenSource cts) { _inner = inner; _cts = cts; }

        public async Task<Guid?> QueueReminderAsync(BookingSession session, string windowKey, string windowLabel, CancellationToken cancellationToken = default)
        {
            var id = await _inner.QueueReminderAsync(session, windowKey, windowLabel, CancellationToken.None);
            _cts.Cancel();
            return id;
        }

        public Task<bool> QueueBookingConfirmedAsync(BookingSession s, CancellationToken ct = default) => _inner.QueueBookingConfirmedAsync(s, ct);
        public Task<bool> QueueBookingCancelledAsync(BookingSession s, CancellationToken ct = default) => _inner.QueueBookingCancelledAsync(s, ct);
        public Task<bool> QueueBookingRescheduledAsync(BookingSession s, DateOnly? d, TimeOnly? t, CancellationToken ct = default) => _inner.QueueBookingRescheduledAsync(s, d, t, ct);
        public Task QueueResendBookingConfirmationAsync(BookingSession s, CancellationToken ct = default) => _inner.QueueResendBookingConfirmationAsync(s, ct);
    }

    private static async Task SweepRemindersAsync(
        BookingTrackerDbContext db, IEmailNotificationService notifications, CancellationTokenSource cts)
    {
        using var provider = ScopeFor(db, notifications);
        var sweeper = new BookingReminderSweeper(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ReminderSettings()),
            NullLogger<BookingReminderSweeper>.Instance);

        // SweepAsync is private; the hosted service's own loop is the public surface.
        // It parks on Task.Delay once the first sweep is done, so cancelling ends it.
        cts.CancelAfter(TimeSpan.FromSeconds(2));
        try { await sweeper.StartAsync(cts.Token); } catch (OperationCanceledException) { }
        try { if (sweeper.ExecuteTask is { } running) await running; } catch (OperationCanceledException) { }
    }

    private static Guid SeedBookingWithOneDueReminder(string database)
    {
        using var db = Context(database);
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);

        var booked = BookingSessionScenarios.StartFillAndSubmit(page.Id);
        db.BookingSessions.Add(booked.Session);
        db.BookingSessionEvents.AddRange(booked.Events);

        // Due now, for a meeting still an hour out - so neither the grace period nor
        // the meeting-already-started rule applies and the sweep must actually queue it.
        db.BookingReminders.Add(BookingReminder.Schedule(booked.Session.Id, page.Id, 60, DateTime.UtcNow.AddHours(1)));
        db.SaveChanges();
        return booked.Session.Id;
    }

    [Fact]
    public async Task AReminderWhoseNotificationIsCommitted_IsRecordedQueuedEvenWhileShuttingDown()
    {
        var database = Guid.NewGuid().ToString();
        SeedBookingWithOneDueReminder(database);

        using (var db = Context(database))
        using (var cts = new CancellationTokenSource())
        {
            await SweepRemindersAsync(db, new ShutsDownOnceQueued(RealNotificationService(db), cts), cts);
        }

        using var check = Context(database);
        var reminder = await check.BookingReminders.AsNoTracking().SingleAsync();
        Assert.Equal(BookingReminderStatus.Queued, reminder.Status);
        Assert.NotNull(reminder.EmailNotificationId);
    }

    [Fact]
    public async Task AnInterruptedReminderSweep_DoesNotQueueASecondCopyAfterTheRestart()
    {
        // REGRESSION GUARD, and the more serious of the two: this defeats the
        // "duplicate delivery is impossible" guarantee without either of the two
        // guards that back it ever being violated. The filtered unique index and
        // MarkQueued both constrain BookingReminders; the duplicate that reached the
        // guest was a second row in EmailNotifications.
        var database = Guid.NewGuid().ToString();
        SeedBookingWithOneDueReminder(database);

        using (var db = Context(database))
        using (var cts = new CancellationTokenSource())
        {
            await SweepRemindersAsync(db, new ShutsDownOnceQueued(RealNotificationService(db), cts), cts);
        }

        using (var db = Context(database))
        using (var cts = new CancellationTokenSource())
        {
            await SweepRemindersAsync(db, RealNotificationService(db), cts);
        }

        using var check = Context(database);
        var guestReminders = await check.EmailNotifications.AsNoTracking()
            .CountAsync(n => n.NotificationType == EmailNotificationType.Reminder);
        Assert.Equal(1, guestReminders);
    }
}
