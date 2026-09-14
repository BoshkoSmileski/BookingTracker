using BookingTracker.Domain.Entities;
using FluentValidation;

namespace BookingTracker.Application.Notifications.Commands.UpdateNotificationSettings;

/// <summary>
/// Fast-fail mirror of NotificationSettings.UpdateSettings' own invariant
/// guard (same "validate at the boundary AND in the domain" pattern used
/// throughout) - only actually
/// exercised when reminders are enabled, same as the domain check.
/// </summary>
public class UpdateNotificationSettingsCommandValidator : AbstractValidator<UpdateNotificationSettingsCommand>
{
    public UpdateNotificationSettingsCommandValidator()
    {
        When(c => c.RemindersEnabled, () =>
        {
            RuleFor(c => c.ReminderMinutesBeforeEvent)
                .NotEmpty().WithMessage("At least one reminder interval is required when reminders are enabled.")
                .Must(m => m.Count <= NotificationSettings.MaxReminderIntervals)
                .WithMessage($"No more than {NotificationSettings.MaxReminderIntervals} reminder intervals are supported.");

            RuleForEach(c => c.ReminderMinutesBeforeEvent)
                .InclusiveBetween(NotificationSettings.MinReminderMinutes, NotificationSettings.MaxReminderMinutes)
                .WithMessage($"Reminder intervals must be between {NotificationSettings.MinReminderMinutes} and {NotificationSettings.MaxReminderMinutes} minutes.");
        });
    }
}
