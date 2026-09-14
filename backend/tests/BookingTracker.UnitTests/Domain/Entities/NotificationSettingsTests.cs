using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Exceptions;

namespace BookingTracker.UnitTests.Domain.Entities;

public class NotificationSettingsTests
{
    [Fact]
    public void CreateDefault_EnablesEverything_WithASingle24HourReminder()
    {
        var settings = NotificationSettings.CreateDefault(Guid.NewGuid());

        Assert.True(settings.NotifyGuestOnBooking);
        Assert.True(settings.NotifyOrganizerOnBooking);
        Assert.True(settings.RemindersEnabled);
        Assert.Equal(new[] { 1440 }, settings.ReminderMinutesBeforeEvent);
    }

    [Fact]
    public void UpdateSettings_RemindersEnabled_WithNoIntervals_Throws()
    {
        var settings = NotificationSettings.CreateDefault(Guid.NewGuid());

        Assert.Throws<DomainException>(() =>
            settings.UpdateSettings(true, true, remindersEnabled: true, reminderMinutesBeforeEvent: []));
    }

    [Fact]
    public void UpdateSettings_TooManyIntervals_Throws()
    {
        var settings = NotificationSettings.CreateDefault(Guid.NewGuid());
        var tooMany = Enumerable.Range(1, NotificationSettings.MaxReminderIntervals + 1).Select(i => i * 60).ToList();

        Assert.Throws<DomainException>(() =>
            settings.UpdateSettings(true, true, remindersEnabled: true, reminderMinutesBeforeEvent: tooMany));
    }

    [Theory]
    [InlineData(1)] // below MinReminderMinutes
    [InlineData(50000)] // above MaxReminderMinutes
    public void UpdateSettings_IntervalOutOfRange_Throws(int minutes)
    {
        var settings = NotificationSettings.CreateDefault(Guid.NewGuid());

        Assert.Throws<DomainException>(() =>
            settings.UpdateSettings(true, true, remindersEnabled: true, reminderMinutesBeforeEvent: [minutes]));
    }

    [Fact]
    public void UpdateSettings_ValidIntervals_AreDeduplicatedAndSorted()
    {
        var settings = NotificationSettings.CreateDefault(Guid.NewGuid());

        settings.UpdateSettings(false, false, remindersEnabled: true, reminderMinutesBeforeEvent: [1440, 60, 60, 30]);

        Assert.Equal(new[] { 30, 60, 1440 }, settings.ReminderMinutesBeforeEvent);
        Assert.False(settings.NotifyGuestOnBooking);
        Assert.False(settings.NotifyOrganizerOnBooking);
    }

    [Fact]
    public void UpdateSettings_DisablingReminders_DoesNotRequireIntervals_AndPreservesThePreviousList()
    {
        var settings = NotificationSettings.CreateDefault(Guid.NewGuid());
        settings.UpdateSettings(true, true, remindersEnabled: true, reminderMinutesBeforeEvent: [60, 1440]);

        // Disabling reminders while passing an empty list must not throw, and should
        // leave the previously-configured intervals intact for if it's re-enabled later.
        settings.UpdateSettings(true, true, remindersEnabled: false, reminderMinutesBeforeEvent: []);

        Assert.False(settings.RemindersEnabled);
        Assert.Equal(new[] { 60, 1440 }, settings.ReminderMinutesBeforeEvent);
    }
}
