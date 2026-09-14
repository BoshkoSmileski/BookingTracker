namespace BookingTracker.Application.Notifications.Dtos;

public record NotificationSettingsDto(
    bool NotifyGuestOnBooking,
    bool NotifyOrganizerOnBooking,
    bool RemindersEnabled,
    IReadOnlyList<int> ReminderMinutesBeforeEvent,
    bool NotifyOrganizerOnReminderSent);
