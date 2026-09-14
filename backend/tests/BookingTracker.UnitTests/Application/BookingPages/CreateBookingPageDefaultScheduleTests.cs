using BookingTracker.Application.Availability.Queries.GetAvailableSlots;
using BookingTracker.Application.BookingPages.Commands.CreateBookingPage;
using BookingTracker.Domain.Common;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.ValueObjects;
using BookingTracker.Infrastructure.Persistence;
using BookingTracker.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookingTracker.UnitTests.Application.BookingPages;

/// <summary>
/// A brand-new organizer's first booking page arrives bookable.
///
/// Creating a page seeds a Monday-Friday 09:00-17:00 schedule when - and only
/// when - the organizer has none. The "only when" half is the one worth
/// guarding: a default that could ever overwrite a real schedule would be a
/// destructive bug hiding inside a convenience, and the case that looks most
/// like an absence (every day switched off) is exactly the one an organizer
/// meant.
/// </summary>
public class CreateBookingPageDefaultScheduleTests
{
    private const string Zone = "Europe/Skopje";

    private static CreateBookingPageCommand Command(Guid organizerId, string? timeZoneId = Zone, string title = "Discovery call")
        => new(organizerId, title, null, 30, 0, 0, null, null, null, timeZoneId);

    private static async Task<Organizer> SeedOrganizerAsync(BookingTrackerDbContext db)
    {
        var organizer = TestEntities.CreateOrganizer();
        db.Organizers.Add(organizer);
        await db.SaveChangesAsync();
        return organizer;
    }

    private static async Task<IReadOnlyList<WorkingDay>> DaysOfAsync(BookingTrackerDbContext db, Guid organizerId)
    {
        var schedule = await db.WorkingSchedules.SingleAsync(s => s.OrganizerId == organizerId);
        return await db.WorkingDays.Where(d => d.WorkingScheduleId == schedule.Id).ToListAsync();
    }

    [Theory]
    [InlineData(DayOfWeek.Monday)]
    [InlineData(DayOfWeek.Tuesday)]
    [InlineData(DayOfWeek.Wednesday)]
    [InlineData(DayOfWeek.Thursday)]
    [InlineData(DayOfWeek.Friday)]
    public async Task FirstBookingPage_OpensEveryWeekdayFromNineToFive(DayOfWeek weekday)
    {
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = await SeedOrganizerAsync(db);

        await new CreateBookingPageCommandHandler(db).Handle(Command(organizer.Id), CancellationToken.None);

        var day = (await DaysOfAsync(db, organizer.Id)).Single(d => d.DayOfWeek == weekday);
        Assert.True(day.IsEnabled);
        var interval = Assert.Single(day.Intervals);
        Assert.Equal(new TimeOnly(9, 0), interval.Start);
        Assert.Equal(new TimeOnly(17, 0), interval.End);
    }

    [Theory]
    [InlineData(DayOfWeek.Saturday)]
    [InlineData(DayOfWeek.Sunday)]
    public async Task FirstBookingPage_LeavesTheWeekendClosed(DayOfWeek weekendDay)
    {
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = await SeedOrganizerAsync(db);

        await new CreateBookingPageCommandHandler(db).Handle(Command(organizer.Id), CancellationToken.None);

        var day = (await DaysOfAsync(db, organizer.Id)).Single(d => d.DayOfWeek == weekendDay);
        Assert.False(day.IsEnabled);
        Assert.Empty(day.Intervals);
    }

    [Fact]
    public async Task FirstBookingPage_MaterializesAllSevenDays()
    {
        // The editor loads a whole week, and "Saturday is closed" is a stored
        // fact rather than a row that happens to be missing.
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = await SeedOrganizerAsync(db);

        await new CreateBookingPageCommandHandler(db).Handle(Command(organizer.Id), CancellationToken.None);

        var days = await DaysOfAsync(db, organizer.Id);
        Assert.Equal(7, days.Count);
        Assert.Equal(7, days.Select(d => d.DayOfWeek).Distinct().Count());
    }

    [Fact]
    public async Task FirstBookingPage_UsesTheOrganizersOwnClock()
    {
        // Seeding 09:00-17:00 against the wrong zone is worse than seeding
        // nothing: every slot a guest is offered silently shifts.
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = await SeedOrganizerAsync(db);

        await new CreateBookingPageCommandHandler(db).Handle(Command(organizer.Id), CancellationToken.None);

        Assert.Equal(Zone, (await db.WorkingSchedules.SingleAsync(s => s.OrganizerId == organizer.Id)).TimeZoneId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Not/AZone")]
    public async Task AnUnusableTimeZoneFallsBackToUtc_RatherThanFailingTheRequest(string? timeZoneId)
    {
        // The zone is a hint the browser supplied, not something the organizer
        // typed, so it must never be able to fail creating a booking page.
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = await SeedOrganizerAsync(db);

        var result = await new CreateBookingPageCommandHandler(db).Handle(
            Command(organizer.Id, timeZoneId), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("UTC", (await db.WorkingSchedules.SingleAsync(s => s.OrganizerId == organizer.Id)).TimeZoneId);
    }

    [Fact]
    public async Task AnExistingScheduleIsNeverReplaced()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = await SeedOrganizerAsync(db);
        var existing = TestEntities.CreateWorkingSchedule(organizer.Id, "America/New_York");
        db.WorkingSchedules.Add(existing);
        db.WorkingDays.Add(WorkingDayFactory.Enabled(existing.Id, DayOfWeek.Saturday, (10, 0, 14, 0)));
        await db.SaveChangesAsync();

        await new CreateBookingPageCommandHandler(db).Handle(Command(organizer.Id), CancellationToken.None);

        var schedule = await db.WorkingSchedules.SingleAsync(s => s.OrganizerId == organizer.Id);
        Assert.Equal(existing.Id, schedule.Id);
        Assert.Equal("America/New_York", schedule.TimeZoneId);
        var day = Assert.Single(await db.WorkingDays.Where(d => d.WorkingScheduleId == schedule.Id).ToListAsync());
        Assert.Equal(DayOfWeek.Saturday, day.DayOfWeek);
    }

    [Fact]
    public async Task AScheduleWithEveryDayClosedIsADecision_NotAnAbsenceToFillIn()
    {
        // The single most dangerous version of this feature: "no bookable
        // availability" must not be read as "no schedule".
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = await SeedOrganizerAsync(db);
        var existing = TestEntities.CreateWorkingSchedule(organizer.Id, Zone);
        db.WorkingSchedules.Add(existing);
        db.WorkingDays.AddRange(Enum.GetValues<DayOfWeek>().Select(d => WorkingDayFactory.Disabled(existing.Id, d)));
        await db.SaveChangesAsync();

        await new CreateBookingPageCommandHandler(db).Handle(Command(organizer.Id), CancellationToken.None);

        var days = await DaysOfAsync(db, organizer.Id);
        Assert.All(days, d => Assert.False(d.IsEnabled));
        Assert.All(days, d => Assert.Empty(d.Intervals));
    }

    [Fact]
    public async Task ASecondBookingPageAddsNoSecondSchedule()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = await SeedOrganizerAsync(db);
        var handler = new CreateBookingPageCommandHandler(db);

        await handler.Handle(Command(organizer.Id, title: "First"), CancellationToken.None);
        await handler.Handle(Command(organizer.Id, "America/New_York", "Second"), CancellationToken.None);

        Assert.Equal(1, await db.WorkingSchedules.CountAsync(s => s.OrganizerId == organizer.Id));
        Assert.Equal(7, (await DaysOfAsync(db, organizer.Id)).Count);
        Assert.Equal(Zone, (await db.WorkingSchedules.SingleAsync(s => s.OrganizerId == organizer.Id)).TimeZoneId);
    }

    [Fact]
    public async Task AnotherOrganizersScheduleDoesNotCountAsThisOnesHaving()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = await SeedOrganizerAsync(db);
        var other = TestEntities.CreateOrganizer("other@example.com");
        db.Organizers.Add(other);
        db.WorkingSchedules.Add(TestEntities.CreateWorkingSchedule(other.Id, Zone));
        await db.SaveChangesAsync();

        await new CreateBookingPageCommandHandler(db).Handle(Command(organizer.Id), CancellationToken.None);

        Assert.Equal(7, (await DaysOfAsync(db, organizer.Id)).Count);
    }

    [Fact]
    public async Task TheDefaultsActuallyProduceBookableSlots()
    {
        // The claim the whole feature rests on, verified where it matters -
        // through the real slot pipeline rather than by reading the rows back.
        //
        // The date is computed rather than written down: slot generation reads
        // DateTime.UtcNow for the same-day cutoff and the booking window, so a
        // literal date silently changes meaning depending on when the suite runs
        //.
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = await SeedOrganizerAsync(db);
        var page = await new CreateBookingPageCommandHandler(db).Handle(
            Command(organizer.Id, "UTC"), CancellationToken.None);

        var handler = new GetAvailableSlotsQueryHandler(
            db, new FakeCalendarSyncService(), NullLogger<GetAvailableSlotsQueryHandler>.Instance);
        var monday = NextWeekdayAtLeastTwoWeeksOut(DayOfWeek.Monday);

        var slots = await handler.Handle(new GetAvailableSlotsQuery(page.Id, monday, monday), CancellationToken.None);

        Assert.NotEmpty(slots);
        Assert.Equal(new TimeOnly(9, 0), slots.Min(s => s.LocalStartTime));
        Assert.Equal(new TimeOnly(17, 0), slots.Max(s => s.LocalEndTime));
    }

    [Fact]
    public async Task TheDefaultsLeaveSundayUnbookable()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = await SeedOrganizerAsync(db);
        var page = await new CreateBookingPageCommandHandler(db).Handle(
            Command(organizer.Id, "UTC"), CancellationToken.None);

        var handler = new GetAvailableSlotsQueryHandler(
            db, new FakeCalendarSyncService(), NullLogger<GetAvailableSlotsQueryHandler>.Instance);
        var sunday = NextWeekdayAtLeastTwoWeeksOut(DayOfWeek.Sunday);

        Assert.Empty(await handler.Handle(new GetAvailableSlotsQuery(page.Id, sunday, sunday), CancellationToken.None));
    }

    [Fact]
    public void TheOneDefinitionOfTheDefaults_IsTheOneTheHandlerUses()
    {
        // If these constants move, the seeded schedule moves with them - which
        // is the entire reason they are not written into the handler.
        var days = WorkingScheduleDefaults.CreateDays(Guid.NewGuid());

        Assert.Equal(5, days.Count(d => d.IsEnabled));
        Assert.All(
            days.Where(d => d.IsEnabled),
            d => Assert.Equal(
                TimeRange.Create(WorkingScheduleDefaults.DayStart, WorkingScheduleDefaults.DayEnd),
                d.Intervals.Single()));
    }

    [Fact]
    public void EachSeededDayGetsItsOwnIntervalInstance()
    {
        // EF Core identifies owned types by reference, so one TimeRange shared
        // across five WorkingDay owners is the failure mode this codebase has
        // already hit once.
        var days = WorkingScheduleDefaults.CreateDays(Guid.NewGuid());

        var intervals = days.Where(d => d.IsEnabled).Select(d => d.Intervals.Single()).ToList();
        Assert.Equal(5, intervals.Distinct(ReferenceEqualityComparer.Instance).Count());
    }

    private static DateOnly NextWeekdayAtLeastTwoWeeksOut(DayOfWeek day)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14);
        while (date.DayOfWeek != day) date = date.AddDays(1);
        return date;
    }
}
