using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using BookingTracker.Application.Notifications.Dtos;
using BookingTracker.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookingTracker.Application.Notifications.Commands.UpdateNotificationSettings;

public class UpdateNotificationSettingsCommandHandler : IRequestHandler<UpdateNotificationSettingsCommand, NotificationSettingsDto>
{
    private readonly IBookingTrackerDbContext _db;
    private readonly IBookingReminderScheduler _reminderScheduler;
    private readonly ILogger<UpdateNotificationSettingsCommandHandler> _logger;

    public UpdateNotificationSettingsCommandHandler(
        IBookingTrackerDbContext db,
        IBookingReminderScheduler reminderScheduler,
        ILogger<UpdateNotificationSettingsCommandHandler> logger)
    {
        _db = db;
        _reminderScheduler = reminderScheduler;
        _logger = logger;
    }

    public async Task<NotificationSettingsDto> Handle(UpdateNotificationSettingsCommand request, CancellationToken cancellationToken)
    {
        var settings = await _db.NotificationSettings.FirstOrDefaultAsync(s => s.OrganizerId == request.OrganizerId, cancellationToken);

        if (settings is null)
        {
            settings = NotificationSettings.CreateDefault(request.OrganizerId);
            _db.NotificationSettings.Add(settings);
        }

        settings.UpdateSettings(
            request.NotifyGuestOnBooking, request.NotifyOrganizerOnBooking, request.RemindersEnabled,
            request.ReminderMinutesBeforeEvent, request.NotifyOrganizerOnReminderSent);

        await _db.SaveChangesAsync(cancellationToken);

        // Best-effort, after the settings are already committed - the same contract
        // every other side effect in this codebase uses. The organizer's saved
        // preference must not be lost because re-aligning existing bookings failed.
        try
        {
            await _reminderScheduler.ResyncForOrganizerAsync(request.OrganizerId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resync reminders after notification settings change for organizer {OrganizerId}.", request.OrganizerId);
        }

        return settings.ToDto();
    }
}
