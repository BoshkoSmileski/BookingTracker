using FluentValidation;

namespace BookingTracker.Application.Calendar.Commands.UpdateCalendarEventSettings;

public class UpdateCalendarEventSettingsCommandValidator : AbstractValidator<UpdateCalendarEventSettingsCommand>
{
    private static readonly string[] ValidVisibilities = ["default", "public", "private"];

    public UpdateCalendarEventSettingsCommandValidator()
    {
        RuleFor(x => x.EventTitleFormat).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DefaultReminderMinutes).InclusiveBetween(0, 40320).When(x => x.DefaultReminderMinutes.HasValue); // Google's own cap: 4 weeks
        RuleFor(x => x.EventVisibility).Must(v => ValidVisibilities.Contains(v))
            .WithMessage($"EventVisibility must be one of: {string.Join(", ", ValidVisibilities)}.");
    }
}
