using BookingTracker.Application.Common;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.UnitTests.TestSupport;

namespace BookingTracker.UnitTests.Application.Common;

/// <summary>
/// BookingConflictChecker is the shared "is this slot still free" gate used by
/// both submit and reschedule. All tests use a real (InMemory) DbContext,
/// since the checker's whole job is a cross-table query - faking it out with
/// mocked DbSets would test nothing meaningful.
/// </summary>
public class BookingConflictCheckerTests
{
    private static readonly DateOnly Date = new(2026, 8, 10);

    [Fact]
    public async Task ExistingSubmittedBooking_AtTheSameTime_BlocksTheSlot()
    {
        // Arrange
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id, durationMinutes: 30);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        await Seed(db, page.Id, hour: 9, minute: 0);
        await db.SaveChangesAsync();

        // Act & Assert
        await Assert.ThrowsAsync<ConflictException>(() =>
            BookingConflictChecker.EnsureSlotIsAvailableAsync(db, Guid.NewGuid(), page.Id, Date, new TimeOnly(9, 0), CancellationToken.None));
    }

    [Fact]
    public async Task NoOverlappingBooking_DoesNotThrow()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id, durationMinutes: 30);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        await Seed(db, page.Id, hour: 9, minute: 0);
        await db.SaveChangesAsync();

        await BookingConflictChecker.EnsureSlotIsAvailableAsync(db, Guid.NewGuid(), page.Id, Date, new TimeOnly(10, 0), CancellationToken.None);
        // No exception = success.
    }

    [Fact]
    public async Task BufferBefore_OnTheCandidatesOwnPage_BlocksAnOtherwiseFreeSlot()
    {
        // Arrange: existing booking 09:00-09:30 on pageA (no buffers). Candidate
        // page has a 30-min buffer-before, so its 09:30 slot's occupied window
        // (09:00-10:00) reaches back into the existing booking, even though the
        // raw 09:30-10:00 window itself doesn't overlap 09:00-09:30.
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = TestEntities.CreateOrganizer();
        var pageA = TestEntities.CreateBookingPage(organizer.Id, durationMinutes: 30, slug: "page-a");
        var pageB = TestEntities.CreateBookingPage(organizer.Id, durationMinutes: 30, bufferBeforeMinutes: 30, slug: "page-b");
        db.Organizers.Add(organizer);
        db.BookingPages.AddRange(pageA, pageB);
        await Seed(db, pageA.Id, hour: 9, minute: 0);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            BookingConflictChecker.EnsureSlotIsAvailableAsync(db, Guid.NewGuid(), pageB.Id, Date, new TimeOnly(9, 30), CancellationToken.None));
    }

    [Fact]
    public async Task BufferAfter_OnTheCandidatesOwnPage_BlocksAnOtherwiseFreeSlot()
    {
        // Arrange: existing booking 10:00-10:30 on pageA (no buffers). Candidate
        // page has a 30-min buffer-after, so its 09:30 slot's occupied window
        // (09:30-10:30) reaches forward into the existing booking.
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = TestEntities.CreateOrganizer();
        var pageA = TestEntities.CreateBookingPage(organizer.Id, durationMinutes: 30, slug: "page-a");
        var pageB = TestEntities.CreateBookingPage(organizer.Id, durationMinutes: 30, bufferAfterMinutes: 30, slug: "page-b");
        db.Organizers.Add(organizer);
        db.BookingPages.AddRange(pageA, pageB);
        await Seed(db, pageA.Id, hour: 10, minute: 0);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ConflictException>(() =>
            BookingConflictChecker.EnsureSlotIsAvailableAsync(db, Guid.NewGuid(), pageB.Id, Date, new TimeOnly(9, 30), CancellationToken.None));
    }

    [Fact]
    public async Task CancelledBooking_IsIgnored()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id, durationMinutes: 30);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        var session = await Seed(db, page.Id, hour: 9, minute: 0);
        session.Cancel(CancelledByType.Customer, null, BookingSessionScenarios.SampleContext);
        await db.SaveChangesAsync();

        await BookingConflictChecker.EnsureSlotIsAvailableAsync(db, Guid.NewGuid(), page.Id, Date, new TimeOnly(9, 0), CancellationToken.None);
        // No exception = the cancelled booking was correctly excluded.
    }

    [Fact]
    public async Task ActiveOrAbandonedSession_IsIgnored_OnlySubmittedCounts()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id, durationMinutes: 30);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        // An Active session that merely has a date/time selected, never submitted.
        var (session, startEvent) = BookingSession.Start(page.Id, BookingSessionScenarios.SampleContext);
        session.SelectDate(Date, 1, BookingSessionScenarios.SampleContext);
        session.SelectTime(new TimeOnly(9, 0), 2, BookingSessionScenarios.SampleContext);
        db.BookingSessions.Add(session);
        db.BookingSessionEvents.Add(startEvent);
        await db.SaveChangesAsync();

        await BookingConflictChecker.EnsureSlotIsAvailableAsync(db, Guid.NewGuid(), page.Id, Date, new TimeOnly(9, 0), CancellationToken.None);
    }

    [Fact]
    public async Task RescheduledBooking_BlocksItsNewTime_NotItsOldTime()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id, durationMinutes: 30);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        var session = await Seed(db, page.Id, hour: 9, minute: 0);
        session.Reschedule(Date, new TimeOnly(14, 0), BookingSessionScenarios.SampleContext);
        await db.SaveChangesAsync();

        // Old 09:00 slot is free again.
        await BookingConflictChecker.EnsureSlotIsAvailableAsync(db, Guid.NewGuid(), page.Id, Date, new TimeOnly(9, 0), CancellationToken.None);

        // New 14:00 slot is now occupied.
        await Assert.ThrowsAsync<ConflictException>(() =>
            BookingConflictChecker.EnsureSlotIsAvailableAsync(db, Guid.NewGuid(), page.Id, Date, new TimeOnly(14, 0), CancellationToken.None));
    }

    [Fact]
    public async Task SessionExcludedById_DoesNotConflictWithItself()
    {
        // The re-check on submit/reschedule passes the session's own id to exclude -
        // otherwise a session would always conflict with its own already-recorded slot.
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id, durationMinutes: 30);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        var session = await Seed(db, page.Id, hour: 9, minute: 0);
        await db.SaveChangesAsync();

        await BookingConflictChecker.EnsureSlotIsAvailableAsync(db, session.Id, page.Id, Date, new TimeOnly(9, 0), CancellationToken.None);
    }

    [Fact]
    public async Task BookingOnADifferentOrganizersPage_NeverConflicts()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var organizerA = TestEntities.CreateOrganizer(email: "a@example.com");
        var organizerB = TestEntities.CreateOrganizer(email: "b@example.com");
        var pageA = TestEntities.CreateBookingPage(organizerA.Id, durationMinutes: 30, slug: "page-a");
        var pageB = TestEntities.CreateBookingPage(organizerB.Id, durationMinutes: 30, slug: "page-b");
        db.Organizers.AddRange(organizerA, organizerB);
        db.BookingPages.AddRange(pageA, pageB);
        await Seed(db, pageA.Id, hour: 9, minute: 0);
        await db.SaveChangesAsync();

        await BookingConflictChecker.EnsureSlotIsAvailableAsync(db, Guid.NewGuid(), pageB.Id, Date, new TimeOnly(9, 0), CancellationToken.None);
    }

    [Fact]
    public async Task UnknownBookingPage_ThrowsNotFound()
    {
        await using var db = InMemoryDbContextFactory.Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            BookingConflictChecker.EnsureSlotIsAvailableAsync(db, Guid.NewGuid(), Guid.NewGuid(), Date, new TimeOnly(9, 0), CancellationToken.None));
    }

    private static async Task<BookingSession> Seed(BookingTracker.Infrastructure.Persistence.BookingTrackerDbContext db, Guid pageId, int hour, int minute)
    {
        var result = BookingSessionScenarios.StartFillAndSubmit(pageId, year: Date.Year, month: Date.Month, day: Date.Day, hour: hour, minute: minute);
        db.BookingSessions.Add(result.Session);
        db.BookingSessionEvents.AddRange(result.Events);
        await db.SaveChangesAsync();
        return result.Session;
    }
}
