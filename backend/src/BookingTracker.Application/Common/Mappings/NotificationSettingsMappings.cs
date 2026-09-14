using BookingTracker.Application.Notifications.Dtos;
using BookingTracker.Domain.Entities;

namespace BookingTracker.Application.Common.Mappings;

public static class NotificationSettingsMappings
{
    public static NotificationSettingsDto ToDto(this NotificationSettings settings) => new(
        settings.NotifyGuestOnBooking,
        settings.NotifyOrganizerOnBooking,
        settings.RemindersEnabled,
        settings.ReminderMinutesBeforeEvent,
        settings.NotifyOrganizerOnReminderSent);
}
