using BookingTracker.Application.Availability.Queries.GetAvailableSlots;
using BookingTracker.Application.Calendar.Dtos;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.Infrastructure.Persistence;
using BookingTracker.UnitTests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookingTracker.UnitTests.Application.Availability;

/// <summary>
/// GetAvailableSlotsQueryHandler is pure orchestration (per its own doc
/// comment: "contains no scheduling logic of its own"), so these tests focus
/// on the wiring - does it load the right rows, apply MaxBookingsPerDay/
/// MaxBookingWindowDays correctly, and layer Google busy intervals on top -
/// rather than re-verifying SlotGenerationService's own rules (covered
/// exhaustively in SlotGenerationServiceTests). Uses a real (InMemory)
/// DbContext and a hand-written ICalendarSyncService fake - no SQL Server,
/// no mocking library.
/// </summary>
public class GetAvailableSlotsQueryHandlerTests
{
    // The handler computes slots against the real DateTime.UtcNow (same-day
    // cutoff, MaxBookingWindowDays), so a hardcoded near-term date would
    // eventually collide with "today" and start intermittently failing
    // (same-day slots get cut off) depending purely on which day the suite
    // happens to run. Always picking a Monday comfortably in the future
    // keeps these tests deterministic regardless of when they're run.
    private static readonly DateOnly Monday = NextMondayAtLeastTwoWeeksOut();

    private static DateOnly NextMondayAtLeastTwoWeeksOut()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(14);
        while (date.DayOfWeek != DayOfWeek.Monday) date = date.AddDays(1);
        return date;
    }

    private sealed record Fixture(
        BookingTrackerDbContext Db, FakeCalendarSyncService Calendar, GetAvailableSlotsQueryHandler Handler,
        Guid OrganizerId, Guid PageId, Guid ScheduleId)
    {
        public void AddWorkingDay(DayOfWeek day, (int StartHour, int StartMinute, int EndHour, int EndMinute) interval)
            => Db.WorkingDays.Add(WorkingDayFactory.Enabled(ScheduleId, day, interval));
    }

    private static async Task<Fixture> Seed(
        int durationMinutes = 30, int? maxBookingsPerDay = null, int? maxBookingWindowDays = null, string timeZoneId = "UTC")
    {
        var db = InMemoryDbContextFactory.Create();
        var calendar = new FakeCalendarSyncService();
        var handler = new GetAvailableSlotsQueryHandler(db, calendar, NullLogger<GetAvailableSlotsQueryHandler>.Instance);

        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(
            organizer.Id, durationMinutes: durationMinutes, maxBookingsPerDay: maxBookingsPerDay, maxBookingWindowDays: maxBookingWindowDays);
        var schedule = TestEntities.CreateWorkingSchedule(organizer.Id, timeZoneId);

        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        db.WorkingSchedules.Add(schedule);
        await db.SaveChangesAsync();

        return new Fixture(db, calendar, handler, organizer.Id, page.Id, schedule.Id);
    }

    [Fact]
    public async Task NoWorkingScheduleConfigured_ReturnsEmpty_NotAnError()
    {
        var db = InMemoryDbContextFactory.Create();
        var handler = new GetAvailableSlotsQueryHandler(db, new FakeCalendarSyncService(), NullLogger<GetAvailableSlotsQueryHandler>.Instance);
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        await db.SaveChangesAsync();

        var result = await handler.Handle(new GetAvailableSlotsQuery(page.Id, Monday, Monday), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task UnknownOrInactiveBookingPage_ThrowsNotFound()
    {
        var db = InMemoryDbContextFactory.Create();
        var handler = new GetAvailableSlotsQueryHandler(db, new FakeCalendarSyncService(), NullLogger<GetAvailableSlotsQueryHandler>.Instance);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetAvailableSlotsQuery(Guid.NewGuid(), Monday, Monday), CancellationToken.None));
    }

    [Fact]
    public async Task WorkingScheduleWithOpenDay_ReturnsSlotsMatchingWorkingHours()
    {
        var fx = await Seed();
        fx.AddWorkingDay(DayOfWeek.Monday, (9, 0, 12, 0));
        await fx.Db.SaveChangesAsync();

        var result = await fx.Handler.Handle(new GetAvailableSlotsQuery(fx.PageId, Monday, Monday), CancellationToken.None);

        Assert.NotEmpty(result);
        Assert.All(result, s => Assert.Equal(Monday, s.LocalDate));
        Assert.Contains(result, s => s.LocalStartTime == new TimeOnly(9, 0));
    }

    [Fact]
    public async Task WholeDayException_ExcludesThatDaysSlots()
    {
        var fx = await Seed();
        fx.AddWorkingDay(DayOfWeek.Monday, (9, 0, 12, 0));
        fx.Db.AvailabilityExceptions.Add(AvailabilityException.Create(fx.OrganizerId, Monday, null, null, AvailabilityExceptionType.Holiday, null));
        await fx.Db.SaveChangesAsync();

        var result = await fx.Handler.Handle(new GetAvailableSlotsQuery(fx.PageId, Monday, Monday), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task MultiDayException_ExcludesEveryDayItSpans()
    {
        var fx = await Seed();
        fx.AddWorkingDay(DayOfWeek.Monday, (9, 0, 12, 0));
        fx.AddWorkingDay(DayOfWeek.Tuesday, (9, 0, 12, 0));
        fx.AddWorkingDay(DayOfWeek.Wednesday, (9, 0, 12, 0));
        fx.Db.AvailabilityExceptions.Add(AvailabilityException.Create(
            fx.OrganizerId, Monday, null, null, AvailabilityExceptionType.Vacation, "Away",
            endDate: Monday.AddDays(1)));
        await fx.Db.SaveChangesAsync();

        var result = await fx.Handler.Handle(
            new GetAvailableSlotsQuery(fx.PageId, Monday, Monday.AddDays(2)), CancellationToken.None);

        // Monday and Tuesday are inside the vacation; Wednesday is the day after it.
        Assert.All(result, s => Assert.Equal(Monday.AddDays(2), s.LocalDate));
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task ExceptionStartingBeforeTheRequestedWindow_StillBlocksTheDaysItReachesInto()
    {
        var fx = await Seed();
        fx.AddWorkingDay(DayOfWeek.Monday, (9, 0, 12, 0));
        fx.AddWorkingDay(DayOfWeek.Tuesday, (9, 0, 12, 0));
        // Starts three days before the window and runs into its first day. Loading
        // exceptions by containment rather than overlap would miss this entirely
        // and quietly offer slots in the middle of a vacation.
        fx.Db.AvailabilityExceptions.Add(AvailabilityException.Create(
            fx.OrganizerId, Monday.AddDays(-3), null, null, AvailabilityExceptionType.Vacation, "Long trip",
            endDate: Monday));
        await fx.Db.SaveChangesAsync();

        var result = await fx.Handler.Handle(
            new GetAvailableSlotsQuery(fx.PageId, Monday, Monday.AddDays(1)), CancellationToken.None);

        Assert.All(result, s => Assert.Equal(Monday.AddDays(1), s.LocalDate));
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task ExistingSubmittedBookingOnAnotherPage_SameOrganizer_ExcludesOverlappingSlot()
    {
        var fx = await Seed();
        fx.AddWorkingDay(DayOfWeek.Monday, (9, 0, 12, 0));
        var otherPage = TestEntities.CreateBookingPage(fx.OrganizerId, durationMinutes: 30, slug: "other-page");
        fx.Db.BookingPages.Add(otherPage);
        var booked = BookingSessionScenarios.StartFillAndSubmit(otherPage.Id, year: Monday.Year, month: Monday.Month, day: Monday.Day, hour: 9, minute: 0);
        fx.Db.BookingSessions.Add(booked.Session);
        fx.Db.BookingSessionEvents.AddRange(booked.Events);
        await fx.Db.SaveChangesAsync();

        var result = await fx.Handler.Handle(new GetAvailableSlotsQuery(fx.PageId, Monday, Monday), CancellationToken.None);

        Assert.DoesNotContain(result, s => s.LocalStartTime == new TimeOnly(9, 0));
        Assert.Contains(result, s => s.LocalStartTime == new TimeOnly(9, 30));
    }

    [Fact]
    public async Task MaxBookingsPerDay_OncePageOwnCapIsReached_ExcludesTheWholeDay()
    {
        var fx = await Seed(maxBookingsPerDay: 1);
        fx.AddWorkingDay(DayOfWeek.Monday, (9, 0, 12, 0));
        var booked = BookingSessionScenarios.StartFillAndSubmit(fx.PageId, year: Monday.Year, month: Monday.Month, day: Monday.Day, hour: 9, minute: 0);
        fx.Db.BookingSessions.Add(booked.Session);
        fx.Db.BookingSessionEvents.AddRange(booked.Events);
        await fx.Db.SaveChangesAsync();

        var result = await fx.Handler.Handle(new GetAvailableSlotsQuery(fx.PageId, Monday, Monday), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task MaxBookingsPerDay_CapNotYetReached_StillReturnsSlots()
    {
        var fx = await Seed(maxBookingsPerDay: 5);
        fx.AddWorkingDay(DayOfWeek.Monday, (9, 0, 12, 0));
        var booked = BookingSessionScenarios.StartFillAndSubmit(fx.PageId, year: Monday.Year, month: Monday.Month, day: Monday.Day, hour: 9, minute: 0);
        fx.Db.BookingSessions.Add(booked.Session);
        fx.Db.BookingSessionEvents.AddRange(booked.Events);
        await fx.Db.SaveChangesAsync();

        var result = await fx.Handler.Handle(new GetAvailableSlotsQuery(fx.PageId, Monday, Monday), CancellationToken.None);

        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task MaxBookingWindowDays_ClampsTheEffectiveToDate()
    {
        // MaxBookingWindowDays is measured from "today" (DateTime.UtcNow), not from the
        // requested FromDate, so this test - uniquely among these - must request starting
        // at today rather than the safely-future Monday every other test uses. Every day in
        // range is opened nearly all-day so the assertion isn't sensitive to what time of
        // day the suite happens to run (same-day cutoff could otherwise hide "today").
        var fx = await Seed(maxBookingWindowDays: 2);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        for (var i = 0; i < 5; i++)
        {
            fx.AddWorkingDay(today.AddDays(i).DayOfWeek, (0, 0, 23, 45));
        }
        await fx.Db.SaveChangesAsync();

        var farFuture = today.AddDays(30);
        var result = await fx.Handler.Handle(new GetAvailableSlotsQuery(fx.PageId, today, farFuture), CancellationToken.None);

        Assert.NotEmpty(result); // tomorrow is safely inside the 2-day window and unaffected by any same-day cutoff
        Assert.All(result, s => Assert.True(s.LocalDate <= today.AddDays(2)));
    }

    [Fact]
    public async Task GoogleBusyInterval_ExcludesOverlappingSlots()
    {
        var fx = await Seed();
        fx.AddWorkingDay(DayOfWeek.Monday, (9, 0, 12, 0));
        await fx.Db.SaveChangesAsync();

        var busyStartUtc = Monday.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc);
        fx.Calendar.BusyIntervalsToReturn = [new CalendarBusyIntervalDto(busyStartUtc, busyStartUtc.AddMinutes(30))];

        var result = await fx.Handler.Handle(new GetAvailableSlotsQuery(fx.PageId, Monday, Monday), CancellationToken.None);

        Assert.DoesNotContain(result, s => s.StartUtc < busyStartUtc.AddMinutes(30) && s.EndUtc > busyStartUtc);
        Assert.NotNull(fx.Calendar.LastGetBusyIntervalsCall);
        Assert.Equal(fx.OrganizerId, fx.Calendar.LastGetBusyIntervalsCall!.Value.OrganizerId);
    }

    [Fact]
    public async Task GoogleReturnsNoBusyIntervals_AllWorkingHourSlotsRemain()
    {
        var fx = await Seed(); // 30-min duration, default 15-min step
        fx.AddWorkingDay(DayOfWeek.Monday, (9, 0, 9, 30)); // fits exactly one slot
        await fx.Db.SaveChangesAsync();
        fx.Calendar.BusyIntervalsToReturn = [];

        var result = await fx.Handler.Handle(new GetAvailableSlotsQuery(fx.PageId, Monday, Monday), CancellationToken.None);

        Assert.Single(result);
    }

    [Fact]
    public async Task TimezoneConversion_ReturnedSlotUtcMatchesTheSchedulesTimeZone()
    {
        var fx = await Seed(timeZoneId: "Europe/Skopje");
        fx.AddWorkingDay(Monday.DayOfWeek, (9, 0, 9, 30)); // fits exactly one slot
        await fx.Db.SaveChangesAsync();

        var result = await fx.Handler.Handle(new GetAvailableSlotsQuery(fx.PageId, Monday, Monday), CancellationToken.None);

        var slot = Assert.Single(result);
        // Derived the same way the production code derives it (TimeZoneInfo.ConvertTimeToUtc)
        // rather than a hardcoded offset, so this stays correct regardless of DST at whatever
        // future date "Monday" resolves to when the suite runs.
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Skopje");
        var expectedLocal = DateTime.SpecifyKind(Monday.ToDateTime(new TimeOnly(9, 0)), DateTimeKind.Unspecified);
        var expectedUtc = TimeZoneInfo.ConvertTimeToUtc(expectedLocal, timeZone);
        Assert.Equal(expectedUtc, slot.StartUtc);
    }
}
