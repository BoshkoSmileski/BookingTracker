using BookingTracker.Application.Bookings.Queries.GetBookingByToken;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.UnitTests.TestSupport;

namespace BookingTracker.UnitTests.Application.Bookings;

/// <summary>
/// The two things <c>PublicBookingDto</c> gained so the guest's own screens stop
/// being dead ends: a way to reach the organizer, and the booking's real
/// instant.
///
/// Both are about a guest holding nothing but a manage link. The screen used to
/// end a past or cancelled booking on "contact the organizer directly" without
/// saying how, and it could not offer an "Add to calendar" at all - the only
/// times it had were organizer-local wall clock, and building an instant out of
/// those in a browser reads them in the *visitor's* zone. That is the exact bug
/// the wizard's own .ics download shipped with, so
/// the fix here is the same one: send the instant the backend already resolved
/// rather than let the client reconstruct it.
/// </summary>
public class PublicBookingContactAndInstantTests
{
    private const string Zone = "Europe/Skopje";

    /// <summary>
    /// Far enough ahead that CanCancel/CanReschedule - and with them the meeting
    /// fields - stay true however long the suite takes to reach this test.
    /// Computed from the clock rather than hardcoded:
    /// the handler compares against DateTime.UtcNow.
    /// </summary>
    private static DateOnly FutureDate() => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);

    private static async Task<(BookingTracker.Domain.Entities.Organizer Organizer, string Token)> SeedAsync(
        BookingTracker.Infrastructure.Persistence.BookingTrackerDbContext db,
        string timeZoneId = Zone,
        int hour = 9,
        int minute = 0,
        int durationMinutes = 30,
        bool withSchedule = true,
        bool cancelled = false)
    {
        var organizer = TestEntities.CreateOrganizer(email: "alex@organizer.example");
        var page = TestEntities.CreateBookingPage(organizer.Id, durationMinutes: durationMinutes, slug: "demo");
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        if (withSchedule) db.WorkingSchedules.Add(TestEntities.CreateWorkingSchedule(organizer.Id, timeZoneId));

        var date = FutureDate();
        var scenario = BookingSessionScenarios.StartFillAndSubmit(
            page.Id, year: date.Year, month: date.Month, day: date.Day, hour: hour, minute: minute);
        if (cancelled) scenario.Session.Cancel(CancelledByType.Customer, "Change of plans", BookingSessionScenarios.SampleContext);

        db.BookingSessions.Add(scenario.Session);
        await db.SaveChangesAsync();

        return (organizer, scenario.Session.PublicToken!);
    }

    // ---------- B3: the organizer's address ----------

    [Fact]
    public async Task ManageBooking_CarriesTheOrganizersEmail()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (organizer, token) = await SeedAsync(db);

        var booking = await new GetBookingByTokenQueryHandler(db).Handle(
            new GetBookingByTokenQuery(token), CancellationToken.None);

        Assert.Equal(organizer.Email, booking.OrganizerEmail);
        Assert.Equal("alex@organizer.example", booking.OrganizerEmail);
    }

    [Fact]
    public async Task ManageBooking_StillCarriesTheOrganizersEmail_WhenTheBookingIsCancelled()
    {
        // The case the field exists for. A cancelled booking has no
        // reschedule, no cancel and no join link left, so the address is the
        // only actionable thing on the screen - it must not be withheld the way
        // MeetingUrl deliberately is.
        await using var db = InMemoryDbContextFactory.Create();
        var (organizer, token) = await SeedAsync(db, cancelled: true);

        var booking = await new GetBookingByTokenQueryHandler(db).Handle(
            new GetBookingByTokenQuery(token), CancellationToken.None);

        Assert.False(booking.CanCancel);
        Assert.False(booking.CanReschedule);
        Assert.Equal(organizer.Email, booking.OrganizerEmail);
    }

    // ---------- B4: the real instant ----------

    [Fact]
    public async Task ManageBooking_ResolvesTheWallClockAgainstTheOrganizersZone()
    {
        // The regression that matters: 09:00 stated on the organizer's clock is
        // 07:00Z in Europe/Skopje's summer offset, and 06:00Z is what a browser
        // in UTC would have produced by parsing "09:00" as its own local time.
        await using var db = InMemoryDbContextFactory.Create();
        var (_, token) = await SeedAsync(db, hour: 9, minute: 0, durationMinutes: 30);

        var booking = await new GetBookingByTokenQueryHandler(db).Handle(
            new GetBookingByTokenQuery(token), CancellationToken.None);

        var expectedStart = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(booking.SelectedDate!.Value.ToDateTime(booking.SelectedTime!.Value), DateTimeKind.Unspecified),
            TimeZoneInfo.FindSystemTimeZoneById(Zone));

        Assert.Equal(expectedStart, booking.StartUtc);
        Assert.Equal(expectedStart.AddMinutes(30), booking.EndUtc);
        // Kind matters on the wire: System.Text.Json only emits a trailing Z for
        // a Utc DateTime, and the frontend reads these without going through
        // lib/dates.ts precisely because they carry one.
        Assert.Equal(DateTimeKind.Utc, booking.StartUtc!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, booking.EndUtc!.Value.Kind);
    }

    [Fact]
    public async Task ManageBooking_EndUtc_IsTheBookingPagesDuration_NotAFixedHalfHour()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (_, token) = await SeedAsync(db, durationMinutes: 45);

        var booking = await new GetBookingByTokenQueryHandler(db).Handle(
            new GetBookingByTokenQuery(token), CancellationToken.None);

        Assert.Equal(45, (booking.EndUtc!.Value - booking.StartUtc!.Value).TotalMinutes);
    }

    [Theory]
    [InlineData("Europe/Skopje")]
    [InlineData("America/New_York")]
    [InlineData("Asia/Tokyo")]
    public async Task ManageBooking_TheInstantAgreesWithTheZoneItIsLabelledWith(string zone)
    {
        // Whatever zone the organizer keeps, the pair the guest is shown
        // (SelectedDate/SelectedTime + TimeZoneId) and the pair a calendar file
        // is built from (StartUtc/EndUtc) must describe the same moment.
        await using var db = InMemoryDbContextFactory.Create();
        var (_, token) = await SeedAsync(db, timeZoneId: zone, hour: 14, minute: 30);

        var booking = await new GetBookingByTokenQueryHandler(db).Handle(
            new GetBookingByTokenQuery(token), CancellationToken.None);

        Assert.Equal(zone, booking.TimeZoneId);
        var backToLocal = TimeZoneInfo.ConvertTimeFromUtc(booking.StartUtc!.Value, TimeZoneInfo.FindSystemTimeZoneById(zone));
        Assert.Equal(booking.SelectedDate!.Value, DateOnly.FromDateTime(backToLocal));
        Assert.Equal(booking.SelectedTime!.Value, TimeOnly.FromDateTime(backToLocal));
    }

    // ---------- B6: reminders the guest is actually going to get ----------

    [Fact]
    public async Task ManageBooking_ReportsTheLeadTimesOfRemindersThatAreActuallyScheduled()
    {
        // Read from the BookingReminders rows themselves, never re-derived from
        // the organizer's NotificationSettings - the reminder-scheduling rule seen
        // from the guest's side. Settings describe what a FUTURE booking would get;
        // only a row proves this booking has one.
        await using var db = InMemoryDbContextFactory.Create();
        var (_, token) = await SeedAsync(db);
        var session = db.BookingSessions.Single();

        // Deliberately out of order, to pin that the DTO sorts them.
        db.BookingReminders.Add(BookingReminder.Schedule(session.Id, session.BookingPageId, 1440, DateTime.UtcNow.AddDays(30)));
        db.BookingReminders.Add(BookingReminder.Schedule(session.Id, session.BookingPageId, 60, DateTime.UtcNow.AddDays(30)));
        await db.SaveChangesAsync();

        var booking = await new GetBookingByTokenQueryHandler(db).Handle(
            new GetBookingByTokenQuery(token), CancellationToken.None);

        Assert.Equal([60, 1440], booking.ReminderLeadMinutes);
    }

    [Fact]
    public async Task ManageBooking_PromisesNoReminder_WhenNoneIsScheduled()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (_, token) = await SeedAsync(db);

        var booking = await new GetBookingByTokenQueryHandler(db).Handle(
            new GetBookingByTokenQuery(token), CancellationToken.None);

        Assert.Empty(booking.ReminderLeadMinutes);
    }

    [Fact]
    public async Task ManageBooking_PromisesNoReminder_ForOneThatIsNoLongerScheduled()
    {
        // Cancelled with the booking, or already handed to the mailer. Neither
        // is a reminder still to come, and "we'll remind you" would be false
        // rather than merely unhelpful.
        await using var db = InMemoryDbContextFactory.Create();
        var (_, token) = await SeedAsync(db);
        var session = db.BookingSessions.Single();

        var cancelled = BookingReminder.Schedule(session.Id, session.BookingPageId, 1440, DateTime.UtcNow.AddDays(30));
        cancelled.Cancel("Booking cancelled");
        var queued = BookingReminder.Schedule(session.Id, session.BookingPageId, 60, DateTime.UtcNow.AddDays(30));
        queued.MarkQueued(Guid.NewGuid());
        db.BookingReminders.AddRange(cancelled, queued);
        await db.SaveChangesAsync();

        var booking = await new GetBookingByTokenQueryHandler(db).Handle(
            new GetBookingByTokenQuery(token), CancellationToken.None);

        Assert.Empty(booking.ReminderLeadMinutes);
    }

    [Fact]
    public async Task ManageBooking_PromisesNoReminder_OnceTheBookingIsCancelled()
    {
        // Belt-and-braces: a terminal booking must claim nothing even if a
        // scheduled row somehow survived the cancellation.
        await using var db = InMemoryDbContextFactory.Create();
        var (_, token) = await SeedAsync(db, cancelled: true);
        var session = db.BookingSessions.Single();
        db.BookingReminders.Add(BookingReminder.Schedule(session.Id, session.BookingPageId, 1440, DateTime.UtcNow.AddDays(30)));
        await db.SaveChangesAsync();

        var booking = await new GetBookingByTokenQueryHandler(db).Handle(
            new GetBookingByTokenQuery(token), CancellationToken.None);

        Assert.Empty(booking.ReminderLeadMinutes);
    }

    [Fact]
    public async Task ManageBooking_WithNoWorkingSchedule_FallsBackToUtcForBothTheLabelAndTheInstant()
    {
        // An organizer who never configured hours. The label already fell back
        // to UTC; the instant has to fall back the same way rather than throw,
        // or a booking made before hours were set becomes unreadable.
        await using var db = InMemoryDbContextFactory.Create();
        var (_, token) = await SeedAsync(db, withSchedule: false, hour: 9, minute: 0);

        var booking = await new GetBookingByTokenQueryHandler(db).Handle(
            new GetBookingByTokenQuery(token), CancellationToken.None);

        Assert.Equal("UTC", booking.TimeZoneId);
        Assert.Equal(booking.SelectedDate!.Value.ToDateTime(booking.SelectedTime!.Value), booking.StartUtc!.Value);
    }
}
