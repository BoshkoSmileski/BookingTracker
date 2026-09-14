using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;

namespace BookingTracker.UnitTests.TestSupport;

/// <summary>Sensible-default factory methods for entities repeated across handler/service tests, so each test only overrides what it actually cares about.</summary>
public static class TestEntities
{
    public static Organizer CreateOrganizer(string email = "organizer@example.com", string passwordHash = "hash")
        => Organizer.Register("Test Organizer", email, passwordHash);

    public static BookingPage CreateBookingPage(
        Guid organizerId,
        int durationMinutes = 30,
        int bufferBeforeMinutes = 0,
        int bufferAfterMinutes = 0,
        string slug = "test-page",
        int? minNoticeMinutes = null,
        int? maxBookingWindowDays = null,
        int? maxBookingsPerDay = null,
        MeetingProviderType meetingProvider = MeetingProviderType.None)
    {
        var page = BookingPage.Create(
            organizerId, slug, "Test Meeting", durationMinutes, bufferBeforeMinutes, bufferAfterMinutes,
            minNoticeMinutes: minNoticeMinutes, maxBookingWindowDays: maxBookingWindowDays, maxBookingsPerDay: maxBookingsPerDay);

        // Defaults to None so every existing caller keeps producing exactly the
        // page it did before meetings existed - a test that is about a meeting
        // has to ask for one.
        if (meetingProvider != MeetingProviderType.None) page.UpdateMeetingSettings(meetingProvider);
        return page;
    }

    public static WorkingSchedule CreateWorkingSchedule(Guid organizerId, string timeZoneId = "UTC")
        => WorkingSchedule.Create(organizerId, timeZoneId);
}
