using BookingTracker.Application.Bookings.Commands.CancelBooking;
using BookingTracker.Application.Notifications;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BookingTracker.UnitTests.Application.Bookings;

/// <summary>
/// End-to-end (handler-level, InMemory-backed) proof that cancelling a
/// booking actually queues notification emails through the real
/// IEmailNotificationService - not just that EmailNotificationService itself
/// behaves correctly in isolation (see EmailNotificationServiceTests), but
/// that CancelBookingCommandHandler is actually wired up to call it. Uses
/// CancelBookingCommandHandler specifically (not Submit/Reschedule) because
/// it's the one booking-lifecycle handler that doesn't need a Serializable
/// transaction, which the InMemory provider doesn't support - see
/// BookingConflictCheckerTests for how the transactional handlers are
/// exercised at the (lower) service level instead.
/// </summary>
public class CancelBookingCommandHandlerTests
{
    [Fact]
    public async Task CancellingASubmittedBooking_QueuesCancellationEmails_ForGuestAndOrganizer()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        var seeded = BookingSessionScenarios.StartFillAndSubmit(page.Id);
        db.BookingSessions.Add(seeded.Session);
        db.BookingSessionEvents.AddRange(seeded.Events);
        await db.SaveChangesAsync();

        var broadcaster = new FakeEventBroadcaster();
        var emailNotificationService = new EmailNotificationService(
            db, new EmailTemplateRenderer(), new CalendarInvitationGenerator(), new FakeFrontendLinkBuilder(),
            Options.Create(new EmailNotificationSettings()), NullLogger<EmailNotificationService>.Instance);
        var calendarSyncService = new FakeCalendarSyncService();
        var reminderScheduler = new FakeBookingReminderScheduler();
        var handler = new CancelBookingCommandHandler(
            db, broadcaster, emailNotificationService, reminderScheduler, calendarSyncService,
            NullLogger<CancelBookingCommandHandler>.Instance);

        var command = new CancelBookingCommand(seeded.Session.Id, null, null, CancelledByType.Customer, "Change of plans", null, null);
        var result = await handler.Handle(command, CancellationToken.None);

        // Cancelling must also stop any future reminders - see BookingReminderScheduler for the real behaviour.
        Assert.Equal(seeded.Session.Id, Assert.Single(reminderScheduler.CancelledFor).SessionId);

        Assert.Equal(nameof(BookingSessionStatus.Cancelled), result.Status);

        var queuedEmails = await db.EmailNotifications.ToListAsync();
        Assert.Equal(2, queuedEmails.Count);
        Assert.Contains(queuedEmails, n => n.NotificationType == EmailNotificationType.CancellationConfirmation && n.ToEmail == seeded.Session.Email);
        Assert.Contains(queuedEmails, n => n.NotificationType == EmailNotificationType.OrganizerCancellationNotice && n.ToEmail == organizer.Email);
        Assert.All(queuedEmails, n => Assert.Equal(EmailNotificationStatus.Pending, n.Status));

        // The booking mutation and the SignalR broadcast still happen regardless of email queueing.
        Assert.Single(broadcaster.BroadcastSessionUpdates);
    }

    [Fact]
    public async Task CancellingABooking_WithOrganizerNotificationsDisabled_OnlyQueuesTheGuestEmail()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        var seeded = BookingSessionScenarios.StartFillAndSubmit(page.Id);
        db.BookingSessions.Add(seeded.Session);
        db.BookingSessionEvents.AddRange(seeded.Events);

        var settings = NotificationSettings.CreateDefault(organizer.Id);
        settings.UpdateSettings(notifyGuestOnBooking: true, notifyOrganizerOnBooking: false, remindersEnabled: true, reminderMinutesBeforeEvent: [1440]);
        db.NotificationSettings.Add(settings);
        await db.SaveChangesAsync();

        var emailNotificationService = new EmailNotificationService(
            db, new EmailTemplateRenderer(), new CalendarInvitationGenerator(), new FakeFrontendLinkBuilder(),
            Options.Create(new EmailNotificationSettings()), NullLogger<EmailNotificationService>.Instance);
        var handler = new CancelBookingCommandHandler(
            db, new FakeEventBroadcaster(), emailNotificationService, new FakeBookingReminderScheduler(),
            new FakeCalendarSyncService(), NullLogger<CancelBookingCommandHandler>.Instance);

        await handler.Handle(new CancelBookingCommand(seeded.Session.Id, null, null, CancelledByType.Customer, null, null, null), CancellationToken.None);

        var queuedEmails = await db.EmailNotifications.ToListAsync();
        var single = Assert.Single(queuedEmails);
        Assert.Equal(EmailNotificationType.CancellationConfirmation, single.NotificationType);
    }
}
