using BookingTracker.Application.Calendar.Dtos;
using MediatR;

namespace BookingTracker.Application.Calendar.Commands.UpdateCalendarEventSettings;

public record UpdateCalendarEventSettingsCommand(
    Guid OrganizerId,
    string EventTitleFormat,
    bool AutoDeleteCancelledBookings,
    bool AutoUpdateRescheduledBookings,
    int? DefaultReminderMinutes,
    string EventVisibility) : IRequest<CalendarConnectionDto>;
