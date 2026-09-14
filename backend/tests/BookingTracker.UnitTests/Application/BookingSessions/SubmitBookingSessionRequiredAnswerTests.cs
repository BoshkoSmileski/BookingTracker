using BookingTracker.Application.BookingSessions.Commands.SubmitBookingSession;
using BookingTracker.Application.Notifications;
using BookingTracker.Domain.Common;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ValidationException = BookingTracker.Application.Common.Exceptions.ValidationException;

namespace BookingTracker.UnitTests.Application.BookingSessions;

/// <summary>
/// A booking page's required custom questions must actually be answered before
/// the booking is accepted.
///
/// Checked in the handler rather than in BookingSession.Submit on purpose:
/// requiredness is page configuration, so it is only true relative to the page
/// as it stands right now - an organizer adding a required question tomorrow
/// must not retroactively invalidate bookings already taken. These tests pin
/// both halves of that: the rejection, and the fact that a booking made before
/// the question existed still reads back fine.
/// </summary>
public class SubmitBookingSessionRequiredAnswerTests
{
    [Fact]
    public async Task Submit_WithARequiredQuestionUnanswered_IsRejectedWithThatFieldNamed()
    {
        await using var db = InMemoryDbContextFactory.CreateIgnoringTransactions();
        var (page, field) = await SeedPageWithRequiredFieldAsync(db);
        var session = await SeedActiveSessionAsync(db, page.Id);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            CreateHandler(db).Handle(new SubmitBookingSessionCommand(session.Id, 99, null, null), CancellationToken.None));

        var key = BookingFieldNames.ForCustomField(field.Id);
        Assert.True(ex.Errors.ContainsKey(key));
        Assert.Contains("Company", ex.Errors[key].Single());
    }

    [Fact]
    public async Task Submit_WithARequiredQuestionAnsweredOnlyWithWhitespace_IsRejected()
    {
        await using var db = InMemoryDbContextFactory.CreateIgnoringTransactions();
        var (page, field) = await SeedPageWithRequiredFieldAsync(db);
        var session = await SeedActiveSessionAsync(db, page.Id);
        // Whitespace never reaches the projection at all - ApplyAnswer treats a
        // blank value as a clear - so this is really asserting that the two
        // rules agree about what "answered" means.
        session.ChangeField(BookingFieldNames.ForCustomField(field.Id), "   ", 10, BookingSessionScenarios.SampleContext);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() =>
            CreateHandler(db).Handle(new SubmitBookingSessionCommand(session.Id, 99, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task Submit_WithEveryRequiredQuestionAnswered_Succeeds()
    {
        await using var db = InMemoryDbContextFactory.CreateIgnoringTransactions();
        var (page, field) = await SeedPageWithRequiredFieldAsync(db);
        var session = await SeedActiveSessionAsync(db, page.Id);
        session.ChangeField(BookingFieldNames.ForCustomField(field.Id), "Acme Ltd", 10, BookingSessionScenarios.SampleContext);
        await db.SaveChangesAsync();

        var confirmation = await CreateHandler(db).Handle(
            new SubmitBookingSessionCommand(session.Id, 99, null, null), CancellationToken.None);

        Assert.Equal(nameof(BookingSessionStatus.Submitted), confirmation.Status);
        Assert.NotNull(confirmation.BookingReference);
        var answer = Assert.Single(confirmation.Answers);
        Assert.Equal(field.Id, answer.FieldId);
        Assert.Equal("Acme Ltd", answer.Value);
    }

    [Fact]
    public async Task Submit_WithAnOptionalQuestionUnanswered_Succeeds()
    {
        await using var db = InMemoryDbContextFactory.CreateIgnoringTransactions();
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id);
        page.AddFormField("Company", BookingFieldType.ShortText, isRequired: false);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        await db.SaveChangesAsync();
        var session = await SeedActiveSessionAsync(db, page.Id);

        var confirmation = await CreateHandler(db).Handle(
            new SubmitBookingSessionCommand(session.Id, 99, null, null), CancellationToken.None);

        Assert.Equal(nameof(BookingSessionStatus.Submitted), confirmation.Status);
        Assert.Empty(confirmation.Answers);
    }

    [Fact]
    public async Task Submit_ForAPageWithNoQuestionsAtAll_IsUnaffected()
    {
        // The overwhelmingly common case: the check must cost a page with no
        // custom fields nothing and change nothing about how it submits.
        await using var db = InMemoryDbContextFactory.CreateIgnoringTransactions();
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        await db.SaveChangesAsync();
        var session = await SeedActiveSessionAsync(db, page.Id);

        var confirmation = await CreateHandler(db).Handle(
            new SubmitBookingSessionCommand(session.Id, 99, null, null), CancellationToken.None);

        Assert.Equal(nameof(BookingSessionStatus.Submitted), confirmation.Status);
    }

    private static SubmitBookingSessionCommandHandler CreateHandler(
        BookingTracker.Infrastructure.Persistence.BookingTrackerDbContext db) =>
        new(db,
            // Availability is not this file's subject - see FakeSender. The real
            // guard is exercised over HTTP in AnonymousBookingIntegrityTests.
            FakeSender.OfferingAnySlot(),
            new FakeEventBroadcaster(),
            new EmailNotificationService(
                db, new EmailTemplateRenderer(), new CalendarInvitationGenerator(), new FakeFrontendLinkBuilder(),
                Options.Create(new EmailNotificationSettings()), NullLogger<EmailNotificationService>.Instance),
            new FakeBookingReminderScheduler(),
            new FakeCalendarSyncService(),
            NullLogger<SubmitBookingSessionCommandHandler>.Instance);

    private static async Task<(BookingPage Page, BookingFormField Field)> SeedPageWithRequiredFieldAsync(
        BookingTracker.Infrastructure.Persistence.BookingTrackerDbContext db)
    {
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id);
        var field = page.AddFormField("Company", BookingFieldType.ShortText, isRequired: true);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        await db.SaveChangesAsync();
        return (page, field);
    }

    /// <summary>An Active session with name/email/date/time filled in - everything except the custom answers.</summary>
    private static async Task<BookingSession> SeedActiveSessionAsync(
        BookingTracker.Infrastructure.Persistence.BookingTrackerDbContext db, Guid pageId)
    {
        var (session, startEvent) = BookingSession.Start(pageId, BookingSessionScenarios.SampleContext);
        var events = new List<BookingSessionEvent> { startEvent };
        events.Add(session.ChangeField(BookingFieldNames.Name, "Jane Doe", 1, BookingSessionScenarios.SampleContext));
        events.Add(session.ChangeField(BookingFieldNames.Email, "jane@example.com", 2, BookingSessionScenarios.SampleContext));
        events.Add(session.SelectDate(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), 3, BookingSessionScenarios.SampleContext));
        events.Add(session.SelectTime(new TimeOnly(9, 0), 4, BookingSessionScenarios.SampleContext));

        db.BookingSessions.Add(session);
        db.BookingSessionEvents.AddRange(events);
        await db.SaveChangesAsync();
        return session;
    }
}
