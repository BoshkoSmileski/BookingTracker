using System.Net;
using System.Net.Mail;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Notifications;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.Domain.ValueObjects;
using BookingTracker.Infrastructure.BackgroundServices;
using BookingTracker.Infrastructure.Persistence;
using BookingTracker.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BookingTracker.UnitTests.Infrastructure.Email;

/// <summary>
/// What one sweep of the email queue does to a notification, per outcome.
///
/// Drives the REAL EmailQueueProcessor over a real (InMemory-backed)
/// DbContext with only the transport faked, because the claim worth pinning is
/// a cross-cutting one - that the classification, the retry decision and the
/// row's resulting state agree. A test of RecordFailedAttempt alone would prove
/// the entity behaves, not that the processor asks it the right question.
///
/// The failures handed to the fake sender are genuine SmtpExceptions produced
/// by a real SmtpClient talking to ScriptedSmtpServer, so nothing here rests on
/// an assumption about what a rejected credential looks like.
/// </summary>
public class EmailQueueProcessorRetryTests
{
    /// <summary>The exception a server rejecting the session's authentication actually produces.</summary>
    private static async Task<Exception> PermanentAuthFailureAsync()
    {
        using var server = ScriptedSmtpServer.RejectingAt("MAIL", "530 5.7.0 Authentication Required");
        return await CaptureAsync(server, withCredentials: true);
    }

    /// <summary>The exception a momentarily unavailable server actually produces.</summary>
    private static async Task<Exception> TransientFailureAsync()
    {
        using var server = ScriptedSmtpServer.RejectingAt("MAIL", "421 4.7.0 Service not available");
        return await CaptureAsync(server);
    }

    private static async Task<Exception> CaptureAsync(ScriptedSmtpServer server, bool withCredentials = false)
    {
        using var client = new SmtpClient("127.0.0.1", server.Port) { EnableSsl = false, Timeout = 5000 };
        if (withCredentials) client.Credentials = new NetworkCredential("user@example.com", "wrong-password");
        using var message = new MailMessage("from@example.com", "to@example.com", "s", "b");

        return await Record.ExceptionAsync(() => client.SendMailAsync(message))
               ?? throw new InvalidOperationException("Expected a send failure.");
    }

    private static EmailNotification Queue(
        BookingTrackerDbContext db,
        Guid? sessionId = null,
        string toEmail = "guest@example.com",
        int maxAttempts = 5)
    {
        var notification = EmailNotification.Create(
            sessionId, EmailNotificationType.BookingConfirmation, toEmail, "Jane Doe",
            "Your booking is confirmed", "<p>html</p>", "text", "BookingConfirmation", maxAttempts);

        db.EmailNotifications.Add(notification);
        db.SaveChanges();
        return notification;
    }

    /// <summary>
    /// Runs exactly one sweep. The processor resolves its dependencies per sweep
    /// from a scope, so a real (if tiny) service provider is what it needs -
    /// handing it the same DbContext instance the test asserts on.
    /// </summary>
    private static async Task SweepAsync(
        BookingTrackerDbContext db, IEmailSender sender, int retryDelaySeconds = 60)
    {
        // Registered as INSTANCES, not factories: the container disposes what it
        // creates, so a factory registration would dispose the test's DbContext
        // the moment the sweep's scope closed and make a second sweep impossible.
        var services = new ServiceCollection();
        services.AddSingleton<IBookingTrackerDbContext>(db);
        services.AddSingleton(sender);
        using var provider = services.BuildServiceProvider();

        var settings = Options.Create(new EmailNotificationSettings { RetryDelaySeconds = retryDelaySeconds });
        var processor = new EmailQueueProcessor(
            provider.GetRequiredService<IServiceScopeFactory>(), settings,
            NullLogger<EmailQueueProcessor>.Instance);

        await processor.ProcessDueNotificationsAsync(CancellationToken.None);
    }

    /// <summary>
    /// Brings a backed-off notification forward so the next sweep sees it as
    /// due. Written through EF's change tracker because NextAttemptAtUtc has a
    /// private setter by design - the alternative is sleeping out a real
    /// backoff, which would add seconds to the suite and buy nothing, since
    /// what these tests are about is the retry DECISION, not the clock.
    /// </summary>
    private static void MakeDueNow(BookingTrackerDbContext db, EmailNotification notification)
    {
        db.Entry(notification).Property(nameof(EmailNotification.NextAttemptAtUtc)).CurrentValue =
            DateTime.UtcNow.AddSeconds(-1);
        db.SaveChanges();
    }

    // ---- 1. A successful send ----------------------------------------------

    [Fact]
    public async Task ASuccessfulSend_MarksTheNotificationSent()
    {
        using var db = InMemoryDbContextFactory.Create();
        var notification = Queue(db);
        var sender = FakeEmailSender.AlwaysSucceeds();

        await SweepAsync(db, sender);

        Assert.Equal(1, sender.SendCount);
        Assert.Equal(EmailNotificationStatus.Sent, notification.Status);
        Assert.NotNull(notification.SentAtUtc);
        Assert.Null(notification.LastError);
    }

    [Fact]
    public async Task ASuccessfulSend_RecordsItOnTheSessionEventLog()
    {
        // The email history is a filter over the session's own event log, so a
        // send that is not logged there is invisible to the organizer.
        using var db = InMemoryDbContextFactory.Create();
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id);
        var (session, startedEvent) = BookingSession.Start(page.Id, ClientContext.Unknown);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        db.BookingSessions.Add(session);
        db.BookingSessionEvents.Add(startedEvent);
        db.SaveChanges();

        Queue(db, sessionId: session.Id);

        await SweepAsync(db, FakeEmailSender.AlwaysSucceeds());

        var logged = await db.BookingSessionEvents
            .Where(e => e.SessionId == session.Id && e.EventType == BookingEventType.EmailSent)
            .ToListAsync();
        Assert.Single(logged);
    }

    // ---- 2. A transient failure still retries ------------------------------

    [Fact]
    public async Task ATransientFailure_StaysPendingAndSchedulesARetry()
    {
        using var db = InMemoryDbContextFactory.Create();
        var notification = Queue(db, maxAttempts: 5);
        var before = DateTime.UtcNow;

        await SweepAsync(db, FakeEmailSender.AlwaysFailsWith(await TransientFailureAsync()));

        Assert.Equal(EmailNotificationStatus.Pending, notification.Status);
        Assert.Equal(1, notification.AttemptCount);
        Assert.NotNull(notification.LastError);
        Assert.True(notification.NextAttemptAtUtc > before, "A retry should have been scheduled in the future.");
    }

    [Fact]
    public async Task ATransientFailure_ThatLaterRecovers_IsSent()
    {
        // The whole point of retrying: the second attempt succeeds and the row
        // ends up Sent, not Failed.
        using var db = InMemoryDbContextFactory.Create();
        var notification = Queue(db, maxAttempts: 5);
        var sender = new FakeEmailSender().ThenFailsWith(await TransientFailureAsync()).ThenSucceeds();

        await SweepAsync(db, sender);
        Assert.Equal(EmailNotificationStatus.Pending, notification.Status);

        MakeDueNow(db, notification);
        await SweepAsync(db, sender);

        Assert.Equal(EmailNotificationStatus.Sent, notification.Status);
        Assert.Equal(2, sender.SendCount);
    }

    // ---- 6. The transient retry budget is unchanged -------------------------

    [Fact]
    public async Task ATransientFailure_StillConsumesExactlyMaxAttempts()
    {
        // REGRESSION GUARD: the permanent-failure path must not have shortened
        // the budget for ordinary failures. Three attempts configured, three
        // attempts made, Failed only on the third.
        using var db = InMemoryDbContextFactory.Create();
        var notification = Queue(db, maxAttempts: 3);
        var sender = FakeEmailSender.AlwaysFailsWith(await TransientFailureAsync());

        await SweepAsync(db, sender);
        Assert.Equal(EmailNotificationStatus.Pending, notification.Status);
        Assert.Equal(1, notification.AttemptCount);

        MakeDueNow(db, notification);
        await SweepAsync(db, sender);
        Assert.Equal(EmailNotificationStatus.Pending, notification.Status);
        Assert.Equal(2, notification.AttemptCount);

        MakeDueNow(db, notification);
        await SweepAsync(db, sender);
        Assert.Equal(EmailNotificationStatus.Failed, notification.Status);
        Assert.Equal(3, notification.AttemptCount);
        Assert.Equal(3, sender.SendCount);
    }

    // ---- 3 & 4. A permanent auth failure does not retry --------------------

    [Fact]
    public async Task APermanentAuthenticationFailure_FailsImmediatelyWithoutRetrying()
    {
        using var db = InMemoryDbContextFactory.Create();
        var notification = Queue(db, maxAttempts: 5);
        var sender = FakeEmailSender.AlwaysFailsWith(await PermanentAuthFailureAsync());

        await SweepAsync(db, sender);

        Assert.Equal(EmailNotificationStatus.Failed, notification.Status);
        Assert.Equal(1, notification.AttemptCount);

        // The decisive assertion: a second sweep must not pick it up again,
        // even though nothing is holding it back but its Failed status.
        await SweepAsync(db, sender);
        Assert.Equal(1, sender.SendCount);
        Assert.Equal(1, notification.AttemptCount);
    }

    [Fact]
    public async Task APermanentAuthenticationFailure_RecordsWhatWentWrong()
    {
        using var db = InMemoryDbContextFactory.Create();
        var notification = Queue(db);

        await SweepAsync(db, FakeEmailSender.AlwaysFailsWith(await PermanentAuthFailureAsync()));

        Assert.Equal(EmailNotificationStatus.Failed, notification.Status);
        Assert.NotNull(notification.LastError);
        Assert.Contains("Authentication Required", notification.LastError);
        // The attempt genuinely happened, so it is counted...
        Assert.Equal(1, notification.AttemptCount);
        // ...and it must never be dressed up as a success.
        Assert.Null(notification.SentAtUtc);
    }

    [Fact]
    public async Task APermanentAuthenticationFailure_LogsNothingOnTheSessionsEventLog()
    {
        // An EmailSent event would tell the organizer's email history that a
        // message went out when none did.
        using var db = InMemoryDbContextFactory.Create();
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id);
        var (session, startedEvent) = BookingSession.Start(page.Id, ClientContext.Unknown);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        db.BookingSessions.Add(session);
        db.BookingSessionEvents.Add(startedEvent);
        db.SaveChanges();

        Queue(db, sessionId: session.Id);

        await SweepAsync(db, FakeEmailSender.AlwaysFailsWith(await PermanentAuthFailureAsync()));

        Assert.Empty(await db.BookingSessionEvents
            .Where(e => e.SessionId == session.Id && e.EventType == BookingEventType.EmailSent)
            .ToListAsync());
    }

    // ---- 5. Unrelated work keeps flowing ------------------------------------

    [Fact]
    public async Task ALaterUnrelatedEmail_IsStillProcessedAfterAPermanentFailure()
    {
        // A bad credential fails the message in front of it; it must not wedge
        // the queue behind it, and once the configuration is fixed the next
        // email goes out normally.
        using var db = InMemoryDbContextFactory.Create();
        var doomed = Queue(db, toEmail: "first@example.com");
        var sender = FakeEmailSender.AlwaysFailsWith(await PermanentAuthFailureAsync());

        await SweepAsync(db, sender);
        Assert.Equal(EmailNotificationStatus.Failed, doomed.Status);

        var later = Queue(db, toEmail: "second@example.com");
        sender.DefaultOutcome = null; // the credential has been corrected

        await SweepAsync(db, sender);

        Assert.Equal(EmailNotificationStatus.Sent, later.Status);
        Assert.Equal("second@example.com", sender.Sent[^1].ToEmail);
        // And the failed one was not revisited.
        Assert.Equal(EmailNotificationStatus.Failed, doomed.Status);
        Assert.Equal(1, doomed.AttemptCount);
    }

    [Fact]
    public async Task OneBadRecipientInABatch_DoesNotStopTheOthers()
    {
        using var db = InMemoryDbContextFactory.Create();
        var first = Queue(db, toEmail: "first@example.com");
        var second = Queue(db, toEmail: "second@example.com");
        var third = Queue(db, toEmail: "third@example.com");

        var sender = new FakeEmailSender()
            .ThenSucceeds()
            .ThenFailsWith(await PermanentAuthFailureAsync())
            .ThenSucceeds();

        await SweepAsync(db, sender);

        Assert.Equal(EmailNotificationStatus.Sent, first.Status);
        Assert.Equal(EmailNotificationStatus.Failed, second.Status);
        Assert.Equal(EmailNotificationStatus.Sent, third.Status);
    }

    [Fact]
    public async Task ANotificationNotYetDue_IsLeftAlone()
    {
        using var db = InMemoryDbContextFactory.Create();
        var notification = Queue(db, maxAttempts: 5);
        // One transient failure pushes it into the future.
        await SweepAsync(db, FakeEmailSender.AlwaysFailsWith(await TransientFailureAsync()), retryDelaySeconds: 600);

        var sender = FakeEmailSender.AlwaysSucceeds();
        await SweepAsync(db, sender);

        Assert.Equal(0, sender.SendCount);
        Assert.Equal(EmailNotificationStatus.Pending, notification.Status);
    }
}
