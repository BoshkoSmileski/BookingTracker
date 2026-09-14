using BookingTracker.Application.BookingPages.Queries.GetBookingPageBySlug;
using BookingTracker.Application.Bookings.Queries.GetBookingByToken;
using BookingTracker.UnitTests.TestSupport;

namespace BookingTracker.UnitTests.Application.BookingPages;

/// <summary>
/// The organizer's time zone reaching the two guest-facing DTOs.
///
/// Every time value this API hands a visitor is organizer-local wall clock -
/// <c>AvailableSlotDto.LocalStartTime</c>, <c>BookingSession.SelectedTime</c>,
/// the confirmation email, the ICS invitation - and until this shipped, nothing
/// told the guest whose clock those readings were on. The booking wizard
/// therefore labelled its slot buttons in the *visitor's* browser zone while
/// recording the *organizer's*, so a guest in another zone could pick "04:00"
/// and be told on the next screen they had booked "10:00".
///
/// The same shape of test as UpdateMeetingSettingsCommandTests'
/// ReachesThePublicBookingPageDto, and for the same reason: a value that is
/// stored but never told to the wizard is the failure mode this codebase has
/// already shipped once.
/// </summary>
public class PublicTimeZoneTests
{
    [Fact]
    public async Task PublicBookingPage_CarriesTheOrganizersTimeZone()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = TestEntities.CreateOrganizer();
        db.Organizers.Add(organizer);
        db.BookingPages.Add(TestEntities.CreateBookingPage(organizer.Id, slug: "demo"));
        db.WorkingSchedules.Add(TestEntities.CreateWorkingSchedule(organizer.Id, "Europe/Skopje"));
        await db.SaveChangesAsync();

        var page = await new GetBookingPageBySlugQueryHandler(db).Handle(
            new GetBookingPageBySlugQuery("demo"), CancellationToken.None);

        Assert.Equal("Europe/Skopje", page.TimeZoneId);
    }

    [Fact]
    public async Task PublicBookingPage_FallsBackToUtcWhenNoScheduleExists()
    {
        // A normal state, not an error: an organizer who has never configured
        // working hours has a page that produces no slots at all, so the label
        // is never seen beside a real time.
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = TestEntities.CreateOrganizer();
        db.Organizers.Add(organizer);
        db.BookingPages.Add(TestEntities.CreateBookingPage(organizer.Id, slug: "demo"));
        await db.SaveChangesAsync();

        var page = await new GetBookingPageBySlugQueryHandler(db).Handle(
            new GetBookingPageBySlugQuery("demo"), CancellationToken.None);

        Assert.Equal("UTC", page.TimeZoneId);
    }

    [Fact]
    public async Task ManageBooking_CarriesTheTimeZoneItsOwnDateAndTimeAreOn()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id, slug: "demo");
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        db.WorkingSchedules.Add(TestEntities.CreateWorkingSchedule(organizer.Id, "Europe/Skopje"));

        // Far enough ahead that the booking stays manageable however long the
        // suite takes to reach this test - CanCancel/CanReschedule, and with
        // them the meeting fields, are gated on the booking being in the future.
        var future = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30);
        var scenario = BookingSessionScenarios.StartFillAndSubmit(
            page.Id, year: future.Year, month: future.Month, day: future.Day, hour: 10, minute: 0);
        db.BookingSessions.Add(scenario.Session);
        await db.SaveChangesAsync();

        var booking = await new GetBookingByTokenQueryHandler(db).Handle(
            new GetBookingByTokenQuery(scenario.Session.PublicToken!), CancellationToken.None);

        Assert.Equal("Europe/Skopje", booking.TimeZoneId);
    }
}
