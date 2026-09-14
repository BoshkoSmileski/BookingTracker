using BookingTracker.Domain.Enums;
using BookingTracker.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.IntegrationTests;

/// <summary>
/// That an HTTP request reaches the notification queue: request -> controller
/// -> handler -> IEmailNotificationService -> an EmailNotifications row.
///
/// Deliberately NOT a second copy of EmailNotificationServiceTests, which
/// already covers every notification type against every settings combination.
/// What only this layer can show is the WIRING - that a real cancel or
/// reschedule request actually runs the best-effort queueing block at all,
/// which a handler test proves only for the handler it constructs directly.
///
/// Nothing is sent: the queue processor is removed from the test host, so rows
/// stay Pending and observable, and the sender is a recorder that would refuse
/// to reach a network even if one ran.
/// </summary>
public class EmailQueueBoundaryTests : ApiTestBase
{
    private const string Slug = "email-page";

    private sealed record Booked(TestData.Workspace Workspace, string Token, Guid SessionId, DateOnly Date, TimeOnly Time);

    private async Task<Booked> ArrangeBookingAsync(bool notifyGuest = true, bool notifyOrganizer = true)
    {
        var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(db, slug: Slug));
        await WithDbAsync(db => TestData.SetNotificationSettingsAsync(
            db, workspace.Organizer.Id, notifyGuest: notifyGuest, notifyOrganizer: notifyOrganizer));

        var date = TestData.NextBookableWeekday();
        var time = await BookingFlow.FirstAvailableTimeAsync(Client, Slug, date);
        await BookingFlow.BookAsync(Client, Slug, date, time, "Jane Doe", "jane@example.com");

        var session = await WithDbAsync(db => db.BookingSessions.SingleAsync());
        return new Booked(workspace, session.PublicToken!, session.Id, date, time);
    }

    private Task<List<Domain.Entities.EmailNotification>> QueuedAsync()
        => WithDbAsync(db => db.EmailNotifications.ToListAsync());

    // ---- Confirmation -------------------------------------------------------

    [Fact]
    public async Task SubmittingABookingQueuesTheGuestConfirmation()
    {
        await ArrangeBookingAsync(notifyGuest: true);

        var queued = await QueuedAsync();

        Assert.Contains(queued, n =>
            n.NotificationType == EmailNotificationType.BookingConfirmation && n.ToEmail == "jane@example.com");
    }

    [Fact]
    public async Task SubmittingABookingQueuesTheOrganizerNotice()
    {
        await ArrangeBookingAsync(notifyOrganizer: true);

        var queued = await QueuedAsync();

        Assert.Contains(queued, n =>
            n.NotificationType == EmailNotificationType.OrganizerNewBooking && n.ToEmail == "organizer@example.com");
    }

    [Fact]
    public async Task NoGuestConfirmationIsQueuedWhenTheOrganizerTurnedItOff()
    {
        await ArrangeBookingAsync(notifyGuest: false, notifyOrganizer: true);

        var queued = await QueuedAsync();

        Assert.DoesNotContain(queued, n => n.NotificationType == EmailNotificationType.BookingConfirmation);
        // The organizer's own copy is a different recipient and is unaffected.
        Assert.Contains(queued, n => n.NotificationType == EmailNotificationType.OrganizerNewBooking);
    }

    [Fact]
    public async Task NoOrganizerNoticeIsQueuedWhenTheOrganizerTurnedItOff()
    {
        await ArrangeBookingAsync(notifyGuest: true, notifyOrganizer: false);

        var queued = await QueuedAsync();

        Assert.DoesNotContain(queued, n => n.NotificationType == EmailNotificationType.OrganizerNewBooking);
        Assert.Contains(queued, n => n.NotificationType == EmailNotificationType.BookingConfirmation);
    }

    [Fact]
    public async Task AQueuedEmailIsPendingAndUnsent()
    {
        // "Queued, never sent" - the distinction the guest-facing copy depends
        // on. Nothing has been transmitted when the request returns.
        await ArrangeBookingAsync();

        var queued = await QueuedAsync();

        Assert.All(queued, n =>
        {
            Assert.Equal(EmailNotificationStatus.Pending, n.Status);
            Assert.Equal(0, n.AttemptCount);
            Assert.Null(n.SentAtUtc);
        });
        Assert.Empty(Factory.Emails.Sent);
    }

    [Fact]
    public async Task TheConfirmationCarriesACalendarInvitation()
    {
        // Rendered at compose time and stored on the row, so the queue only
        // carries it across. Reminders deliberately carry none - not asserted
        // here, since no reminder is due during a test.
        await ArrangeBookingAsync();

        var confirmation = (await QueuedAsync())
            .Single(n => n.NotificationType == EmailNotificationType.BookingConfirmation);

        Assert.True(confirmation.HasCalendarInvitation);
        Assert.Equal("REQUEST", confirmation.IcsMethod);
    }

    // ---- Cancellation -------------------------------------------------------

    [Fact]
    public async Task CancellingOverHttpQueuesTheCancellationEmails()
    {
        var booked = await ArrangeBookingAsync();
        var before = (await QueuedAsync()).Count;

        await Client.PostAsync($"/api/bookings/{booked.Token}/cancel", RawJson("""{"reason":"Changed plans"}"""));

        var queued = await QueuedAsync();
        Assert.True(queued.Count > before, "Cancelling should have queued at least one email.");
        Assert.Contains(queued, n => n.NotificationType == EmailNotificationType.CancellationConfirmation);
        Assert.Contains(queued, n => n.NotificationType == EmailNotificationType.OrganizerCancellationNotice);
    }

    [Fact]
    public async Task TheCancellationCarriesACancelInvitation()
    {
        // METHOD:CANCEL is what removes the event from the guest's calendar;
        // a REQUEST here would leave it in place.
        var booked = await ArrangeBookingAsync();

        await Client.PostAsync($"/api/bookings/{booked.Token}/cancel", RawJson("""{"reason":null}"""));

        var cancellation = (await QueuedAsync())
            .Single(n => n.NotificationType == EmailNotificationType.CancellationConfirmation);

        Assert.True(cancellation.HasCalendarInvitation);
        Assert.Equal("CANCEL", cancellation.IcsMethod);
    }

    // ---- Reschedule ---------------------------------------------------------

    [Fact]
    public async Task ReschedulingOverHttpQueuesTheRescheduleEmails()
    {
        var booked = await ArrangeBookingAsync();
        var newDate = booked.Date.AddDays(booked.Date.DayOfWeek == DayOfWeek.Friday ? 3 : 1);
        var newTime = await BookingFlow.FirstAvailableTimeAsync(Client, Slug, newDate);

        await Client.PostAsync($"/api/bookings/{booked.Token}/reschedule",
            RawJson($$"""{"newDate":"{{newDate:yyyy-MM-dd}}","newTime":"{{newTime:HH:mm:ss}}"}"""));

        var queued = await QueuedAsync();
        Assert.Contains(queued, n => n.NotificationType == EmailNotificationType.RescheduleConfirmation);
        Assert.Contains(queued, n => n.NotificationType == EmailNotificationType.OrganizerRescheduleNotice);
    }

    // ---- Nothing escapes ----------------------------------------------------

    [Fact]
    public async Task NothingIsEverActuallySentDuringAnIntegrationTest()
    {
        // The isolation guarantee, asserted rather than assumed: across a full
        // booking, cancellation and everything they queue, IEmailSender is
        // never called - because nothing drains the queue in the test host.
        var booked = await ArrangeBookingAsync();
        await Client.PostAsync($"/api/bookings/{booked.Token}/cancel", RawJson("""{"reason":null}"""));

        Assert.NotEmpty(await QueuedAsync());
        Assert.Empty(Factory.Emails.Sent);
    }
}
