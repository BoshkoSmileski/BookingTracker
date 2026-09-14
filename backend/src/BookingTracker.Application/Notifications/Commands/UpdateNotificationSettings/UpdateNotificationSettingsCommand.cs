using BookingTracker.Application.Notifications.Dtos;
using MediatR;

namespace BookingTracker.Application.Notifications.Commands.UpdateNotificationSettings;

public record UpdateNotificationSettingsCommand(
    Guid OrganizerId,
    bool NotifyGuestOnBooking,
    bool NotifyOrganizerOnBooking,
    bool RemindersEnabled,
    IReadOnlyList<int> ReminderMinutesBeforeEvent,
    bool NotifyOrganizerOnReminderSent) : IRequest<NotificationSettingsDto>;
