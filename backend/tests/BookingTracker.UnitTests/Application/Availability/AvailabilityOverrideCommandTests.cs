using BookingTracker.Application.Availability.Commands.DeleteAvailabilityOverride;
using BookingTracker.Application.Availability.Commands.SaveAvailabilityOverride;
using BookingTracker.Application.Availability.Dtos;
using BookingTracker.Application.Availability.Queries.GetAvailabilityOverrides;
using BookingTracker.Application.Availability.Queries.GetAvailableSlots;
using BookingTracker.Application.Calendar.Dtos;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookingTracker.UnitTests.Application.Availability;

/// <summary>
/// The organizer-facing CQRS slice for date overrides, plus the end of the
/// chain that actually matters: that saving one changes the slots a guest is
/// offered on the public booking page. Real (InMemory) DbContext throughout -
/// the upsert behaviour is a cross-row claim a mocked DbSet would not exercise.
/// </summary>
public class AvailabilityOverrideCommandTests
{
    private static readonly DateOnly Monday = NextMondayAtLeastTwoWeeksOut();

    // Same rule as GetAvailableSlotsQueryHandlerTests: the slot path reads the
    // real clock, so the date is computed rather than hardcoded.
    private static DateOnly NextMondayAtLeastTwoWeeksOut()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14);
        while (date.DayOfWeek != DayOfWeek.Monday) date = date.AddDays(1);
        return date;
    }

    private static TimeRangeDto Range(int startHour, int endHour) =>
        new(new TimeOnly(startHour, 0), new TimeOnly(endHour, 0));

    // ---------- save / upsert ----------

    [Fact]
    public async Task Save_CreatesTheOverrideAndReturnsIt()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = await SeedOrganizerAsync(db);

        var result = await new SaveAvailabilityOverrideCommandHandler(db).Handle(
            new SaveAvailabilityOverrideCommand(organizer.Id, Monday, [Range(13, 17)], "Afternoon only"),
            CancellationToken.None);

        Assert.Equal(Monday, result.Date);
        Assert.False(result.IsClosed);
        Assert.Equal(new TimeOnly(13, 0), Assert.Single(result.Ranges).Start);
        Assert.Equal("Afternoon only", result.Note);
    }

    [Fact]
    public async Task Save_ForADateThatAlreadyHasOne_ReplacesItRatherThanAddingASecond()
    {
        // The whole reason this is one upsert command rather than a
        // create/update pair: at most one override can exist per date.
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = await SeedOrganizerAsync(db);
        var handler = new SaveAvailabilityOverrideCommandHandler(db);

        await handler.Handle(new SaveAvailabilityOverrideCommand(organizer.Id, Monday, [Range(9, 17)], null), CancellationToken.None);
        var second = await handler.Handle(
            new SaveAvailabilityOverrideCommand(organizer.Id, Monday, [Range(13, 15)], "Changed"), CancellationToken.None);

        Assert.Single(await db.AvailabilityOverrides.ToListAsync());
        Assert.Equal(new TimeOnly(13, 0), Assert.Single(second.Ranges).Start);
        Assert.Equal("Changed", second.Note);
    }

    [Fact]
    public async Task Save_WithNoRanges_ClosesTheDay()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = await SeedOrganizerAsync(db);

        var result = await new SaveAvailabilityOverrideCommandHandler(db).Handle(
            new SaveAvailabilityOverrideCommand(organizer.Id, Monday, [], "Christmas Eve"), CancellationToken.None);

        Assert.True(result.IsClosed);
        Assert.Empty(result.Ranges);
    }

    [Fact]
    public async Task Save_StoresMultipleRanges()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = await SeedOrganizerAsync(db);

        var result = await new SaveAvailabilityOverrideCommandHandler(db).Handle(
            new SaveAvailabilityOverrideCommand(organizer.Id, Monday, [Range(13, 15), Range(9, 12)], null),
            CancellationToken.None);

        Assert.Equal([new TimeOnly(9, 0), new TimeOnly(13, 0)], result.Ranges.Select(r => r.Start));
    }

    [Fact]
    public async Task Save_ScopesToTheCallingOrganizer()
    {
        // Two organizers can each hold an override for the same date without
        // colliding - the uniqueness is per (organizer, date), not per date.
        await using var db = InMemoryDbContextFactory.Create();
        var first = await SeedOrganizerAsync(db);
        var second = await SeedOrganizerAsync(db, "second@example.com");
        var handler = new SaveAvailabilityOverrideCommandHandler(db);

        await handler.Handle(new SaveAvailabilityOverrideCommand(first.Id, Monday, [Range(9, 12)], null), CancellationToken.None);
        await handler.Handle(new SaveAvailabilityOverrideCommand(second.Id, Monday, [Range(13, 17)], null), CancellationToken.None);

        Assert.Equal(2, await db.AvailabilityOverrides.CountAsync());
    }

    // ---------- query ----------

    [Fact]
    public async Task Get_ReturnsTheOrganizersOwnOverridesInDateOrder()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = await SeedOrganizerAsync(db);
        var other = await SeedOrganizerAsync(db, "other@example.com");
        var handler = new SaveAvailabilityOverrideCommandHandler(db);

        await handler.Handle(new SaveAvailabilityOverrideCommand(organizer.Id, Monday.AddDays(2), [Range(9, 12)], null), CancellationToken.None);
        await handler.Handle(new SaveAvailabilityOverrideCommand(organizer.Id, Monday, [Range(9, 12)], null), CancellationToken.None);
        await handler.Handle(new SaveAvailabilityOverrideCommand(other.Id, Monday, [Range(9, 12)], null), CancellationToken.None);

        var result = await new GetAvailabilityOverridesQueryHandler(db).Handle(
            new GetAvailabilityOverridesQuery(organizer.Id), CancellationToken.None);

        Assert.Equal([Monday, Monday.AddDays(2)], result.Select(o => o.Date));
    }

    [Fact]
    public async Task Get_FiltersByDateRange()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = await SeedOrganizerAsync(db);
        var handler = new SaveAvailabilityOverrideCommandHandler(db);
        await handler.Handle(new SaveAvailabilityOverrideCommand(organizer.Id, Monday, [Range(9, 12)], null), CancellationToken.None);
        await handler.Handle(new SaveAvailabilityOverrideCommand(organizer.Id, Monday.AddDays(30), [Range(9, 12)], null), CancellationToken.None);

        var result = await new GetAvailabilityOverridesQueryHandler(db).Handle(
            new GetAvailabilityOverridesQuery(organizer.Id, Monday, Monday.AddDays(7)), CancellationToken.None);

        Assert.Equal(Monday, Assert.Single(result).Date);
    }

    // ---------- delete ----------

    [Fact]
    public async Task Delete_RemovesTheOverrideSoTheDateFallsBackToTheWeeklySchedule()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = await SeedOrganizerAsync(db);
        var saved = await new SaveAvailabilityOverrideCommandHandler(db).Handle(
            new SaveAvailabilityOverrideCommand(organizer.Id, Monday, [Range(13, 17)], null), CancellationToken.None);

        await new DeleteAvailabilityOverrideCommandHandler(db).Handle(
            new DeleteAvailabilityOverrideCommand(organizer.Id, saved.Id), CancellationToken.None);

        Assert.Empty(await db.AvailabilityOverrides.ToListAsync());
    }

    [Fact]
    public async Task Delete_OfAnotherOrganizersOverride_IsForbidden()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var owner = await SeedOrganizerAsync(db);
        var intruder = await SeedOrganizerAsync(db, "intruder@example.com");
        var saved = await new SaveAvailabilityOverrideCommandHandler(db).Handle(
            new SaveAvailabilityOverrideCommand(owner.Id, Monday, [Range(13, 17)], null), CancellationToken.None);

        await Assert.ThrowsAsync<ForbiddenException>(() => new DeleteAvailabilityOverrideCommandHandler(db).Handle(
            new DeleteAvailabilityOverrideCommand(intruder.Id, saved.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_OfAnUnknownOverride_IsNotFound()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = await SeedOrganizerAsync(db);

        await Assert.ThrowsAsync<NotFoundException>(() => new DeleteAvailabilityOverrideCommandHandler(db).Handle(
            new DeleteAvailabilityOverrideCommand(organizer.Id, Guid.NewGuid()), CancellationToken.None));
    }

    // ---------- the end of the chain: what a guest is offered ----------

    [Fact]
    public async Task SavingAnOverride_ChangesTheSlotsThePublicBookingPageOffers()
    {
        // The claim that matters. Everything above is plumbing if this is not
        // true - and it is the failure the booking-instructions feature actually
        // shipped with: a setting stored but never reaching the wizard.
        var (db, handler, organizer, pageId) = await SeedSlotFixtureAsync();

        var before = await handler.Handle(new GetAvailableSlotsQuery(pageId, Monday, Monday), CancellationToken.None);
        Assert.Equal(new TimeOnly(9, 0), before.First().LocalStartTime);

        await new SaveAvailabilityOverrideCommandHandler(db).Handle(
            new SaveAvailabilityOverrideCommand(organizer.Id, Monday, [Range(13, 17)], null), CancellationToken.None);

        var after = await handler.Handle(new GetAvailableSlotsQuery(pageId, Monday, Monday), CancellationToken.None);
        Assert.Equal(new TimeOnly(13, 0), after.First().LocalStartTime);
        Assert.DoesNotContain(after, s => s.LocalStartTime < new TimeOnly(13, 0));
    }

    [Fact]
    public async Task AClosedOverride_LeavesThePublicBookingPageWithNoSlotsThatDay()
    {
        var (db, handler, organizer, pageId) = await SeedSlotFixtureAsync();

        await new SaveAvailabilityOverrideCommandHandler(db).Handle(
            new SaveAvailabilityOverrideCommand(organizer.Id, Monday, [], "Closed"), CancellationToken.None);

        var result = await handler.Handle(new GetAvailableSlotsQuery(pageId, Monday, Monday), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task AnOverride_OpensASaturdayTheWeeklyScheduleDoesNotCover()
    {
        var (db, handler, organizer, pageId) = await SeedSlotFixtureAsync();
        var saturday = Monday.AddDays(5);

        Assert.Empty(await handler.Handle(new GetAvailableSlotsQuery(pageId, saturday, saturday), CancellationToken.None));

        await new SaveAvailabilityOverrideCommandHandler(db).Handle(
            new SaveAvailabilityOverrideCommand(organizer.Id, saturday, [Range(9, 12)], null), CancellationToken.None);

        var result = await handler.Handle(new GetAvailableSlotsQuery(pageId, saturday, saturday), CancellationToken.None);
        Assert.NotEmpty(result);
        Assert.All(result, s => Assert.Equal(saturday, s.LocalDate));
    }

    [Fact]
    public async Task GoogleBusyIntervals_StillRemoveSlotsFromOverrideHours()
    {
        var (db, handler, organizer, pageId, calendar) = await SeedSlotFixtureWithCalendarAsync();
        await new SaveAvailabilityOverrideCommandHandler(db).Handle(
            new SaveAvailabilityOverrideCommand(organizer.Id, Monday, [Range(13, 17)], null), CancellationToken.None);

        // Busy 13:00-14:00 local (= UTC here), which must remove exactly the
        // slots the override had just opened.
        var busyStart = Monday.ToDateTime(new TimeOnly(13, 0));
        calendar.BusyIntervalsToReturn =
        [
            new CalendarBusyIntervalDto(
                DateTime.SpecifyKind(busyStart, DateTimeKind.Utc),
                DateTime.SpecifyKind(busyStart.AddHours(1), DateTimeKind.Utc))
        ];

        var result = await handler.Handle(new GetAvailableSlotsQuery(pageId, Monday, Monday), CancellationToken.None);

        Assert.DoesNotContain(result, s => s.LocalStartTime < new TimeOnly(14, 0));
        Assert.Contains(result, s => s.LocalStartTime == new TimeOnly(14, 0));
    }

    [Fact]
    public async Task ABlockedDate_StillWinsOverAnOverrideThatOpenedTheDay()
    {
        var (db, handler, organizer, pageId) = await SeedSlotFixtureAsync();
        var saturday = Monday.AddDays(5);

        await new SaveAvailabilityOverrideCommandHandler(db).Handle(
            new SaveAvailabilityOverrideCommand(organizer.Id, saturday, [Range(9, 12)], null), CancellationToken.None);
        db.AvailabilityExceptions.Add(AvailabilityException.Create(
            organizer.Id, saturday, null, null, AvailabilityExceptionType.Holiday, "Public holiday"));
        await db.SaveChangesAsync();

        var result = await handler.Handle(new GetAvailableSlotsQuery(pageId, saturday, saturday), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task MaxBookingsPerDay_StillCapsADayAnOverrideOpened()
    {
        var (db, handler, organizer, pageId) = await SeedSlotFixtureAsync(maxBookingsPerDay: 1);
        var saturday = Monday.AddDays(5);

        await new SaveAvailabilityOverrideCommandHandler(db).Handle(
            new SaveAvailabilityOverrideCommand(organizer.Id, saturday, [Range(9, 12)], null), CancellationToken.None);
        Assert.NotEmpty(await handler.Handle(new GetAvailableSlotsQuery(pageId, saturday, saturday), CancellationToken.None));

        var booking = BookingSessionScenarios.StartFillAndSubmit(
            pageId, year: saturday.Year, month: saturday.Month, day: saturday.Day, hour: 9, minute: 0);
        db.BookingSessions.Add(booking.Session);
        await db.SaveChangesAsync();

        var result = await handler.Handle(new GetAvailableSlotsQuery(pageId, saturday, saturday), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ABookingMadeIntoAnOverrideSlot_StillRebuildsFromItsEventLog()
    {
        // Overrides are organizer CONFIGURATION, not booking-session state, so
        // they are deliberately not event-sourced (event sourcing governs
        // facts about a BookingSession). This pins that adding them changed
        // nothing about the projection-equals-replay guarantee.
        var (db, _, organizer, pageId) = await SeedSlotFixtureAsync();
        var saturday = Monday.AddDays(5);
        await new SaveAvailabilityOverrideCommandHandler(db).Handle(
            new SaveAvailabilityOverrideCommand(organizer.Id, saturday, [Range(9, 12)], null), CancellationToken.None);

        var booking = BookingSessionScenarios.StartFillAndSubmit(
            pageId, year: saturday.Year, month: saturday.Month, day: saturday.Day, hour: 9, minute: 0);

        var rebuilt = BookingSession.Rebuild(pageId, booking.Events.OrderBy(e => e.ClientSequenceNumber));

        Assert.Equal(booking.Session.Status, rebuilt.Status);
        Assert.Equal(booking.Session.SelectedDate, rebuilt.SelectedDate);
        Assert.Equal(booking.Session.SelectedTime, rebuilt.SelectedTime);
        Assert.Equal(booking.Session.BookingReference, rebuilt.BookingReference);
    }

    // ---------- fixtures ----------

    private static async Task<Organizer> SeedOrganizerAsync(
        BookingTracker.Infrastructure.Persistence.BookingTrackerDbContext db, string email = "organizer@example.com")
    {
        var organizer = TestEntities.CreateOrganizer(email);
        db.Organizers.Add(organizer);
        await db.SaveChangesAsync();
        return organizer;
    }

    /// <summary>Mon-Fri 09:00-17:00, 60-minute service - the schedule every example in the brief starts from.</summary>
    private static async Task<(BookingTracker.Infrastructure.Persistence.BookingTrackerDbContext Db,
        GetAvailableSlotsQueryHandler Handler, Organizer Organizer, Guid PageId)> SeedSlotFixtureAsync(int? maxBookingsPerDay = null)
    {
        var fixture = await SeedSlotFixtureWithCalendarAsync(maxBookingsPerDay);
        return (fixture.Db, fixture.Handler, fixture.Organizer, fixture.PageId);
    }

    private static async Task<(BookingTracker.Infrastructure.Persistence.BookingTrackerDbContext Db,
        GetAvailableSlotsQueryHandler Handler, Organizer Organizer, Guid PageId, FakeCalendarSyncService Calendar)>
        SeedSlotFixtureWithCalendarAsync(int? maxBookingsPerDay = null)
    {
        var db = InMemoryDbContextFactory.Create();
        var calendar = new FakeCalendarSyncService();
        var handler = new GetAvailableSlotsQueryHandler(db, calendar, NullLogger<GetAvailableSlotsQueryHandler>.Instance);

        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id, durationMinutes: 60, maxBookingsPerDay: maxBookingsPerDay);
        var schedule = TestEntities.CreateWorkingSchedule(organizer.Id);

        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        db.WorkingSchedules.Add(schedule);
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
        {
            db.WorkingDays.Add(WorkingDayFactory.Enabled(schedule.Id, day, (9, 0, 17, 0)));
        }
        await db.SaveChangesAsync();

        return (db, handler, organizer, page.Id, calendar);
    }
}
