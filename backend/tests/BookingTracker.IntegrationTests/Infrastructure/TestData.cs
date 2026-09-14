using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.Domain.ValueObjects;
using BookingTracker.Infrastructure.Persistence;

namespace BookingTracker.IntegrationTests.Infrastructure;

/// <summary>
/// Seeds a test's starting state directly into the test host's own database.
///
/// Written through the domain's real factory methods rather than raw rows, so
/// what a test starts from is state the application itself could have produced.
/// Nothing here goes near the development database - see
/// <see cref="BookingTrackerApiFactory"/>, which points the context at a
/// uniquely-named InMemory database per test class.
/// </summary>
public static class TestData
{
    /// <summary>Everything a public booking flow needs: an organizer, a page, and a bookable week.</summary>
    public sealed record Workspace(Organizer Organizer, BookingPage Page, WorkingSchedule Schedule);

    public static async Task<Organizer> AddOrganizerAsync(
        BookingTrackerDbContext db, string email = "organizer@example.com", string name = "Test Organizer")
    {
        var organizer = Organizer.Register(name, email, "hashed-password-not-used-by-these-tests");
        db.Organizers.Add(organizer);
        await db.SaveChangesAsync();
        return organizer;
    }

    /// <summary>
    /// An organizer with one active booking page and Monday-Friday 09:00-17:00
    /// availability in <paramref name="timeZoneId"/>.
    /// </summary>
    public static async Task<Workspace> AddWorkspaceAsync(
        BookingTrackerDbContext db,
        string email = "organizer@example.com",
        string slug = "test-page",
        string timeZoneId = "UTC",
        int durationMinutes = 30,
        int? maxBookingsPerDay = null,
        MeetingProviderType meetingProvider = MeetingProviderType.None)
    {
        var organizer = await AddOrganizerAsync(db, email);

        var page = BookingPage.Create(
            organizer.Id, slug, "Test Meeting", durationMinutes,
            bufferBeforeMinutes: 0, bufferAfterMinutes: 0,
            maxBookingsPerDay: maxBookingsPerDay);
        if (meetingProvider != MeetingProviderType.None) page.UpdateMeetingSettings(meetingProvider);
        db.BookingPages.Add(page);

        var schedule = WorkingSchedule.Create(organizer.Id, timeZoneId);
        db.WorkingSchedules.Add(schedule);

        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            var isWorkday = day is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
            db.WorkingDays.Add(WorkingDay.Create(
                schedule.Id, day, isWorkday,
                // A fresh TimeRange per day: owned types are tracked BY REFERENCE
                // by EF, so reusing one instance across days is a real bug this
                // repository has already hit once.
                isWorkday ? [TimeRange.Create(new TimeOnly(9, 0), new TimeOnly(17, 0))] : []));
        }

        await db.SaveChangesAsync();
        return new Workspace(organizer, page, schedule);
    }

    /// <summary>
    /// A weekday far enough ahead to clear the same-day cutoff and any minimum
    /// notice, and comfortably inside the default booking window.
    ///
    /// Computed from DateTime.UtcNow rather than hardcoded: slot generation reads the real clock, so a fixed date silently
    /// changes meaning as the wall clock rolls forward.
    /// </summary>
    public static DateOnly NextBookableWeekday(int minimumDaysAhead = 14)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(minimumDaysAhead);
        while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) date = date.AddDays(1);
        return date;
    }

    /// <summary>The next Saturday - a day the seeded weekly schedule leaves closed.</summary>
    public static DateOnly NextClosedSaturday(int minimumDaysAhead = 14)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(minimumDaysAhead);
        while (date.DayOfWeek != DayOfWeek.Saturday) date = date.AddDays(1);
        return date;
    }

    public static async Task SetNotificationSettingsAsync(
        BookingTrackerDbContext db,
        Guid organizerId,
        bool notifyGuest = true,
        bool notifyOrganizer = true,
        bool remindersEnabled = false)
    {
        var settings = NotificationSettings.CreateDefault(organizerId);
        settings.UpdateSettings(notifyGuest, notifyOrganizer, remindersEnabled, [1440], notifyOrganizerOnReminderSent: false);
        db.NotificationSettings.Add(settings);
        await db.SaveChangesAsync();
    }
}
