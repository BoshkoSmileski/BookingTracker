using BookingTracker.Application.Notifications;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BookingTracker.UnitTests.Application.Notifications;

/// <summary>
/// EmailNotificationService is the one place that decides who gets notified
/// (per NotificationSettings) and turns that decision into queued
/// EmailNotification rows - these tests cover both the queueing itself and
/// the notification-service business logic, against a real (InMemory)
/// DbContext, the real EmailTemplateRenderer (pure, no I/O), and a hand-written
/// IFrontendLinkBuilder fake. Nothing here ever sends an email - that's
/// EmailQueueProcessor's job, deliberately out of scope for these tests.
/// </summary>
public class EmailNotificationServiceTests
{
    private static EmailNotificationService CreateService(BookingTracker.Infrastructure.Persistence.BookingTrackerDbContext db, int maxRetries = 3) =>
        new(db, new EmailTemplateRenderer(), new CalendarInvitationGenerator(), new FakeFrontendLinkBuilder(),
            Options.Create(new EmailNotificationSettings { MaxRetries = maxRetries }),
            NullLogger<EmailNotificationService>.Instance);

    private static async Task<(Organizer Organizer, BookingPage Page, BookingSession Session)> SeedSubmittedBookingAsync(
        BookingTracker.Infrastructure.Persistence.BookingTrackerDbContext db)
    {
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);

        var result = BookingSessionScenarios.StartFillAndSubmit(page.Id);
        db.BookingSessions.Add(result.Session);
        db.BookingSessionEvents.AddRange(result.Events);
        await db.SaveChangesAsync();

        return (organizer, page, result.Session);
    }

    [Fact]
    public async Task QueueBookingConfirmedAsync_DefaultSettings_QueuesBothGuestAndOrganizerEmails()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (organizer, _, session) = await SeedSubmittedBookingAsync(db);
        var service = CreateService(db, maxRetries: 5);

        await service.QueueBookingConfirmedAsync(session);

        var queued = await db.EmailNotifications.ToListAsync();
        Assert.Equal(2, queued.Count);

        var guestEmail = Assert.Single(queued, n => n.NotificationType == EmailNotificationType.BookingConfirmation);
        Assert.Equal(session.Email, guestEmail.ToEmail);
        Assert.Equal(EmailNotificationStatus.Pending, guestEmail.Status);
        Assert.Equal(5, guestEmail.MaxAttempts);
        Assert.Equal(session.Id, guestEmail.BookingSessionId);

        var organizerEmail = Assert.Single(queued, n => n.NotificationType == EmailNotificationType.OrganizerNewBooking);
        Assert.Equal(organizer.Email, organizerEmail.ToEmail);
    }

    [Fact]
    public async Task QueueBookingConfirmedAsync_GuestNotificationsDisabled_OnlyQueuesTheOrganizerEmail()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (organizer, _, session) = await SeedSubmittedBookingAsync(db);

        var settings = NotificationSettings.CreateDefault(organizer.Id);
        settings.UpdateSettings(notifyGuestOnBooking: false, notifyOrganizerOnBooking: true, remindersEnabled: true, reminderMinutesBeforeEvent: [1440]);
        db.NotificationSettings.Add(settings);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        await service.QueueBookingConfirmedAsync(session);

        var queued = await db.EmailNotifications.ToListAsync();
        var single = Assert.Single(queued);
        Assert.Equal(EmailNotificationType.OrganizerNewBooking, single.NotificationType);
    }

    [Fact]
    public async Task QueueBookingConfirmedAsync_OrganizerNotificationsDisabled_OnlyQueuesTheGuestEmail()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (organizer, _, session) = await SeedSubmittedBookingAsync(db);

        var settings = NotificationSettings.CreateDefault(organizer.Id);
        settings.UpdateSettings(notifyGuestOnBooking: true, notifyOrganizerOnBooking: false, remindersEnabled: true, reminderMinutesBeforeEvent: [1440]);
        db.NotificationSettings.Add(settings);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        await service.QueueBookingConfirmedAsync(session);

        var queued = await db.EmailNotifications.ToListAsync();
        var single = Assert.Single(queued);
        Assert.Equal(EmailNotificationType.BookingConfirmation, single.NotificationType);
    }

    [Fact]
    public async Task QueueBookingCancelledAsync_QueuesCancellationEmails_ToGuestAndOrganizer_WithReason()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (_, _, session) = await SeedSubmittedBookingAsync(db);
        session.Cancel(CancelledByType.Customer, "Schedule conflict", BookingSessionScenarios.SampleContext);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        await service.QueueBookingCancelledAsync(session);

        var queued = await db.EmailNotifications.ToListAsync();
        Assert.Equal(2, queued.Count);
        Assert.Contains(queued, n => n.NotificationType == EmailNotificationType.CancellationConfirmation);
        Assert.Contains(queued, n => n.NotificationType == EmailNotificationType.OrganizerCancellationNotice);
        Assert.All(queued, n => Assert.Contains("Schedule conflict", n.HtmlBody));
    }

    [Fact]
    public async Task QueueBookingRescheduledAsync_QueuesRescheduleEmails_ToGuestAndOrganizer()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (_, _, session) = await SeedSubmittedBookingAsync(db);
        var oldDate = session.SelectedDate;
        var oldTime = session.SelectedTime;
        session.Reschedule(new DateOnly(2026, 8, 20), new TimeOnly(14, 0), BookingSessionScenarios.SampleContext);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        await service.QueueBookingRescheduledAsync(session, oldDate, oldTime);

        var queued = await db.EmailNotifications.ToListAsync();
        Assert.Equal(2, queued.Count);
        Assert.Contains(queued, n => n.NotificationType == EmailNotificationType.RescheduleConfirmation);
        Assert.Contains(queued, n => n.NotificationType == EmailNotificationType.OrganizerRescheduleNotice);
    }

    [Fact]
    public async Task QueueReminderAsync_QueuesASingleReminderEmail_TaggedWithTheGivenWindowKey()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (_, _, session) = await SeedSubmittedBookingAsync(db);

        var service = CreateService(db);
        await service.QueueReminderAsync(session, "24h", "24 hours");

        var queued = await db.EmailNotifications.ToListAsync();
        var single = Assert.Single(queued);
        Assert.Equal(EmailNotificationType.Reminder, single.NotificationType);
        Assert.Equal("24h", single.EventLogFieldName);
        Assert.Contains("24 hours", single.Subject);
    }

    [Fact]
    public async Task QueueResendBookingConfirmationAsync_QueuesTheGuestEmail_EvenWhenGuestNotificationsAreDisabled()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (organizer, _, session) = await SeedSubmittedBookingAsync(db);

        var settings = NotificationSettings.CreateDefault(organizer.Id);
        settings.UpdateSettings(notifyGuestOnBooking: false, notifyOrganizerOnBooking: false, remindersEnabled: false, reminderMinutesBeforeEvent: []);
        db.NotificationSettings.Add(settings);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        await service.QueueResendBookingConfirmationAsync(session);

        var queued = await db.EmailNotifications.ToListAsync();
        var single = Assert.Single(queued);
        Assert.Equal(EmailNotificationType.BookingConfirmation, single.NotificationType);
        Assert.Equal(session.Email, single.ToEmail);
    }

    [Fact]
    public async Task QueueBookingConfirmedAsync_SessionMissingRequiredFields_QueuesNothing_AndDoesNotThrow()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var page = TestEntities.CreateBookingPage(Guid.NewGuid());
        var (session, startEvent) = BookingSession.Start(page.Id, BookingSessionScenarios.SampleContext);
        db.BookingPages.Add(page);
        db.BookingSessions.Add(session);
        db.BookingSessionEvents.Add(startEvent);
        await db.SaveChangesAsync();

        var service = CreateService(db);
        await service.QueueBookingConfirmedAsync(session); // Active session, never submitted - no email/date/reference yet.

        Assert.Empty(await db.EmailNotifications.ToListAsync());
    }

    [Fact]
    public async Task QueueBookingConfirmedAsync_PutsTheCustomAnswers_InBothRecipientsEmails()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id);
        var company = page.AddFormField("Company", BookingFieldType.ShortText, isRequired: true);
        var topic = page.AddFormField("What would you like to discuss?", BookingFieldType.LongText, isRequired: false);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);

        var seeded = BookingSessionScenarios.StartFillAnswerAndSubmit(
            page.Id, (company.Id, "Acme Ltd"), (topic.Id, "Pricing for next quarter"));
        db.BookingSessions.Add(seeded.Session);
        db.BookingSessionEvents.AddRange(seeded.Events);
        await db.SaveChangesAsync();

        await CreateService(db).QueueBookingConfirmedAsync(seeded.Session);

        var queued = await db.EmailNotifications.ToListAsync();
        Assert.Equal(2, queued.Count);
        foreach (var notification in queued)
        {
            foreach (var body in new[] { notification.HtmlBody, notification.TextBody })
            {
                Assert.Contains("Company", body);
                Assert.Contains("Acme Ltd", body);
                Assert.Contains("What would you like to discuss?", body);
                Assert.Contains("Pricing for next quarter", body);
            }
        }
    }

    [Fact]
    public async Task QueueReminderAsync_DoesNotRepeatTheAnswers()
    {
        // Reminders stay short deliberately - the same call the ICS attachment
        // makes. The guest was already told all of this at confirmation.
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id);
        var company = page.AddFormField("Company", BookingFieldType.ShortText, isRequired: false);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);

        var seeded = BookingSessionScenarios.StartFillAnswerAndSubmit(page.Id, (company.Id, "Acme Ltd"));
        db.BookingSessions.Add(seeded.Session);
        db.BookingSessionEvents.AddRange(seeded.Events);
        await db.SaveChangesAsync();

        await CreateService(db).QueueReminderAsync(seeded.Session, "24h", "24 hours");

        var reminder = Assert.Single(await db.EmailNotifications.ToListAsync());
        Assert.DoesNotContain("Acme Ltd", reminder.HtmlBody);
        Assert.DoesNotContain("Acme Ltd", reminder.TextBody);
    }

    [Fact]
    public async Task QueueBookingConfirmedAsync_SkipsAnAnswerWhoseFieldTheOrganizerHasSinceRemoved()
    {
        // The answer stays on the booking as history, but there is no longer a
        // label to caption it with, so the email simply omits it.
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id);
        var company = page.AddFormField("Company", BookingFieldType.ShortText, isRequired: false);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);

        var seeded = BookingSessionScenarios.StartFillAnswerAndSubmit(page.Id, (company.Id, "Acme Ltd"));
        db.BookingSessions.Add(seeded.Session);
        db.BookingSessionEvents.AddRange(seeded.Events);
        await db.SaveChangesAsync();

        page.RemoveFormField(company.Id);
        await db.SaveChangesAsync();

        await CreateService(db).QueueBookingConfirmedAsync(seeded.Session);

        Assert.Single(seeded.Session.Answers);
        foreach (var notification in await db.EmailNotifications.ToListAsync())
        {
            Assert.DoesNotContain("Acme Ltd", notification.HtmlBody);
        }
    }

    [Fact]
    public async Task QueueBookingCancelledAsync_PutsThePageSOwnPublicBookingUrlInTheGuestSEmail()
    {
        // The template-level assertions live in EmailBrandingTests; this is the
        // wiring - that the URL reaching a real queued row is the one
        // IFrontendLinkBuilder builds from THIS page's slug, not a second
        // URL-assembly path invented in the templates.
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id, slug: "coffee-chat");
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);

        var seeded = BookingSessionScenarios.SubmitThenCancel(page.Id);
        db.BookingSessions.Add(seeded.Session);
        db.BookingSessionEvents.AddRange(seeded.Events);
        await db.SaveChangesAsync();

        await CreateService(db).QueueBookingCancelledAsync(seeded.Session);

        var guest = Assert.Single(
            await db.EmailNotifications.ToListAsync(),
            n => n.NotificationType == EmailNotificationType.CancellationConfirmation);
        Assert.Contains("https://test.local/book/coffee-chat", guest.HtmlBody);
        Assert.Contains("Book another time: https://test.local/book/coffee-chat", guest.TextBody);

        // The organizer's copy of the same cancellation is a notice about their
        // own page, so it carries no rebooking action.
        var organizerNotice = Assert.Single(
            await db.EmailNotifications.ToListAsync(),
            n => n.NotificationType == EmailNotificationType.OrganizerCancellationNotice);
        Assert.DoesNotContain("Book another time", organizerNotice.HtmlBody);
    }

    [Fact]
    public async Task QueueBookingCancelledAsync_ForASecondPage_LinksThatPage_NotTheFirst()
    {
        // The URL has to belong to the booking page that was actually booked.
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = TestEntities.CreateOrganizer();
        var first = TestEntities.CreateBookingPage(organizer.Id, slug: "intro-call");
        var second = TestEntities.CreateBookingPage(organizer.Id, slug: "deep-dive");
        db.Organizers.Add(organizer);
        db.BookingPages.AddRange(first, second);

        var seeded = BookingSessionScenarios.SubmitThenCancel(second.Id);
        db.BookingSessions.Add(seeded.Session);
        db.BookingSessionEvents.AddRange(seeded.Events);
        await db.SaveChangesAsync();

        await CreateService(db).QueueBookingCancelledAsync(seeded.Session);

        var guest = Assert.Single(
            await db.EmailNotifications.ToListAsync(),
            n => n.NotificationType == EmailNotificationType.CancellationConfirmation);
        Assert.Contains("https://test.local/book/deep-dive", guest.HtmlBody);
        Assert.DoesNotContain("intro-call", guest.HtmlBody);
    }
}
