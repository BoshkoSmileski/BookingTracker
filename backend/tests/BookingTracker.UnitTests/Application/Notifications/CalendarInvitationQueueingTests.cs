using BookingTracker.Application.Notifications;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.Domain.Exceptions;
using BookingTracker.Infrastructure.Persistence;
using BookingTracker.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BookingTracker.UnitTests.Application.Notifications;

/// <summary>
/// Which queued emails carry an invitation, and what it says. Complements
/// CalendarInvitationGeneratorTests (identity rules) and
/// IcsCalendarInvitationTests (format) by covering the wiring: the queue is
/// still the delivery mechanism, so the invitation has to be persisted on the
/// notification row at compose time.
/// </summary>
public class CalendarInvitationQueueingTests
{
    private static EmailNotificationService CreateService(BookingTrackerDbContext db) =>
        new(db, new EmailTemplateRenderer(), new CalendarInvitationGenerator(), new FakeFrontendLinkBuilder(),
            Options.Create(new EmailNotificationSettings()), NullLogger<EmailNotificationService>.Instance);

    private static async Task<(Organizer Organizer, BookingSession Session)> SeedAsync(BookingTrackerDbContext db)
    {
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        db.WorkingSchedules.Add(TestEntities.CreateWorkingSchedule(organizer.Id, "UTC"));

        var result = BookingSessionScenarios.StartFillAndSubmit(page.Id, year: 2026, month: 8, day: 20, hour: 9, minute: 0);
        db.BookingSessions.Add(result.Session);
        db.BookingSessionEvents.AddRange(result.Events);
        await db.SaveChangesAsync();
        return (organizer, result.Session);
    }

    private static string Value(string ics, string name) =>
        ics.Replace("\r\n ", string.Empty)
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .First(l => l.StartsWith(name + ":", StringComparison.Ordinal))[(name.Length + 1)..];

    [Fact]
    public async Task Confirmation_AttachesARequestToBothRecipients()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (_, session) = await SeedAsync(db);

        await CreateService(db).QueueBookingConfirmedAsync(session);

        var queued = await db.EmailNotifications.ToListAsync();
        Assert.Equal(2, queued.Count);
        Assert.All(queued, n =>
        {
            Assert.True(n.HasCalendarInvitation);
            Assert.Equal("REQUEST", n.IcsMethod);
            Assert.Equal("invite.ics", n.IcsFileName);
            Assert.Contains("BEGIN:VCALENDAR", n.IcsContent);
        });
        // Both recipients get the same event, so the UID must match.
        Assert.Single(queued.Select(n => Value(n.IcsContent!, "UID")).Distinct());
    }

    [Fact]
    public async Task Cancellation_AttachesACancelToBothRecipients()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (_, session) = await SeedAsync(db);
        session.Cancel(CancelledByType.Customer, "no longer needed", BookingSessionScenarios.SampleContext);
        await db.SaveChangesAsync();

        await CreateService(db).QueueBookingCancelledAsync(session);

        var queued = await db.EmailNotifications.ToListAsync();
        Assert.Equal(2, queued.Count);
        Assert.All(queued, n =>
        {
            Assert.Equal("CANCEL", n.IcsMethod);
            Assert.Equal("CANCELLED", Value(n.IcsContent!, "STATUS"));
        });
    }

    [Fact]
    public async Task Reschedule_AttachesARequestWithTheSameUidAndAHigherSequence()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (_, session) = await SeedAsync(db);
        var service = CreateService(db);

        await service.QueueBookingConfirmedAsync(session);
        var original = await db.EmailNotifications.FirstAsync(n => n.NotificationType == EmailNotificationType.BookingConfirmation);

        var oldDate = session.SelectedDate;
        var oldTime = session.SelectedTime;
        session.Reschedule(new DateOnly(2026, 8, 25), new TimeOnly(14, 0), BookingSessionScenarios.SampleContext);
        await db.SaveChangesAsync();
        await service.QueueBookingRescheduledAsync(session, oldDate, oldTime);

        var updated = await db.EmailNotifications.FirstAsync(n => n.NotificationType == EmailNotificationType.RescheduleConfirmation);

        Assert.Equal("REQUEST", updated.IcsMethod);
        Assert.Equal(Value(original.IcsContent!, "UID"), Value(updated.IcsContent!, "UID"));
        Assert.True(int.Parse(Value(updated.IcsContent!, "SEQUENCE")) > int.Parse(Value(original.IcsContent!, "SEQUENCE")));
        Assert.Equal("20260825T140000Z", Value(updated.IcsContent!, "DTSTART"));
    }

    [Fact]
    public async Task Reminders_CarryNoInvitation()
    {
        // Deliberate: the guest already holds the event from the confirmation, and a
        // reminder per configured interval would attach the same file repeatedly.
        await using var db = InMemoryDbContextFactory.Create();
        var (_, session) = await SeedAsync(db);

        await CreateService(db).QueueReminderAsync(session, "24h", "24 hours");

        var reminder = await db.EmailNotifications.SingleAsync();
        Assert.False(reminder.HasCalendarInvitation);
        Assert.Null(reminder.IcsContent);
        Assert.Null(reminder.IcsMethod);
    }

    [Fact]
    public async Task Resend_OfAnActiveBooking_AttachesTheCurrentRequest()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (_, session) = await SeedAsync(db);
        session.Reschedule(new DateOnly(2026, 8, 25), new TimeOnly(14, 0), BookingSessionScenarios.SampleContext);
        await db.SaveChangesAsync();

        await CreateService(db).QueueResendBookingConfirmationAsync(session);

        var resent = await db.EmailNotifications.SingleAsync();
        Assert.Equal("REQUEST", resent.IcsMethod);
        // Reflects the booking as it stands now, not as first booked.
        Assert.Equal("20260825T140000Z", Value(resent.IcsContent!, "DTSTART"));
        Assert.Equal("1", Value(resent.IcsContent!, "SEQUENCE"));
    }

    [Fact]
    public async Task Resend_OfACancelledBooking_AttachesTheCancellation()
    {
        // A recipient who lost the original must end up with the event removed, not
        // re-added.
        await using var db = InMemoryDbContextFactory.Create();
        var (_, session) = await SeedAsync(db);
        session.Cancel(CancelledByType.Organizer, null, BookingSessionScenarios.SampleContext);
        await db.SaveChangesAsync();

        await CreateService(db).QueueResendBookingConfirmationAsync(session);

        var resent = await db.EmailNotifications.SingleAsync();
        Assert.Equal("CANCEL", resent.IcsMethod);
        Assert.Equal("CANCELLED", Value(resent.IcsContent!, "STATUS"));
    }

    [Fact]
    public async Task InvitationIsPersistedOnTheQueueRow_SoDeliveryStaysAsynchronous()
    {
        // The queue remains responsible for delivery: whatever was composed must
        // survive to send time without regeneration.
        await using var db = InMemoryDbContextFactory.Create();
        var (_, session) = await SeedAsync(db);

        await CreateService(db).QueueBookingConfirmedAsync(session);

        await using var reread = InMemoryDbContextFactory.Create();
        var stored = await db.EmailNotifications.AsNoTracking().FirstAsync();
        Assert.EndsWith("END:VCALENDAR\r\n", stored.IcsContent);
        Assert.Equal(EmailNotificationStatus.Pending, stored.Status);
    }

    [Fact]
    public void EmailNotification_RejectsAHalfPopulatedInvitation()
    {
        // A nameless or method-less calendar part is silently rejected by clients, so
        // the entity refuses to hold one.
        Assert.Throws<DomainException>(() => EmailNotification.Create(
            Guid.NewGuid(), EmailNotificationType.BookingConfirmation, "a@example.com", "A",
            "subject", "<p>html</p>", "text", "BookingConfirmation", 3,
            icsContent: "BEGIN:VCALENDAR", icsFileName: null, icsMethod: "REQUEST"));
    }

    [Fact]
    public void EmailNotification_WithoutAnInvitation_IsValidAndReportsNone()
    {
        var notification = EmailNotification.Create(
            Guid.NewGuid(), EmailNotificationType.Reminder, "a@example.com", "A",
            "subject", "<p>html</p>", "text", "24h", 3);

        Assert.False(notification.HasCalendarInvitation);
    }
}
