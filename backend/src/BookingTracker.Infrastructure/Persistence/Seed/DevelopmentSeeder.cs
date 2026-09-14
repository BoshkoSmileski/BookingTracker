using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Infrastructure.Persistence.Seed;

/// <summary>
/// Creates one demo organizer (with a known login), booking page, and a
/// Mon-Fri 09:00-12:00/13:00-17:00 working schedule so both the frontend and
/// manual API testing have real availability to query out of the box. Not
/// meant for production use.
/// </summary>
public static class DevelopmentSeeder
{
    public const string DemoBookingPageSlug = "demo-30-min-meeting";
    public const string DemoOrganizerEmail = "organizer@example.com";
    public const string DemoOrganizerPassword = "Passw0rd!";
    public const string DemoTimeZoneId = "Europe/Skopje";

    private static readonly DayOfWeek[] WeekdayWorkingDays =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday
    ];

    public static async Task SeedAsync(BookingTrackerDbContext db, IPasswordHasher passwordHasher)
    {
        // Guarded on the ORGANIZER, not on the booking page, because the organizer
        // is the row that cannot be removed through the product: a booking page
        // with no confirmed bookings is deletable (DeleteBookingPageCommandHandler,
        // 204), and the demo page starts out in exactly that state. Guarding on the
        // slug therefore let an ordinary delete desynchronise the check from the
        // insert - the next start passed the guard, re-inserted organizer@example.com
        // and hit IX_Organizers_Email, so the API refused to start (SQL Server 2601)
        // and kept refusing until someone deleted the row by hand.
        //
        // This is also the check the request path already makes: RegisterCommandHandler
        // asks Organizers for the email it is about to insert, rather than asking a
        // different table for a proxy.
        if (await db.Organizers.AnyAsync(o => o.Email == DemoOrganizerEmail)) return;

        var passwordHash = passwordHasher.HashPassword(DemoOrganizerPassword);
        var organizer = Organizer.Register("Demo Organizer", DemoOrganizerEmail, passwordHash);
        var page = BookingPage.Create(
            organizer.Id, DemoBookingPageSlug, "30 Minute Meeting", 30,
            bufferBeforeMinutes: 0, bufferAfterMinutes: 15,
            description: "A focused 30-minute call to discuss your project and next steps.");

        var schedule = WorkingSchedule.Create(organizer.Id, DemoTimeZoneId);

        var morning = TimeRange.Create(new TimeOnly(9, 0), new TimeOnly(12, 0));
        var afternoon = TimeRange.Create(new TimeOnly(13, 0), new TimeOnly(17, 0));

        var days = Enum.GetValues<DayOfWeek>().Select(dayOfWeek =>
        {
            var isWorkingDay = WeekdayWorkingDays.Contains(dayOfWeek);
            var intervals = isWorkingDay ? new[] { morning, afternoon } : [];
            return WorkingDay.Create(schedule.Id, dayOfWeek, isWorkingDay, intervals);
        }).ToList();

        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        db.WorkingSchedules.Add(schedule);
        db.WorkingDays.AddRange(days);
        await db.SaveChangesAsync();
    }
}
