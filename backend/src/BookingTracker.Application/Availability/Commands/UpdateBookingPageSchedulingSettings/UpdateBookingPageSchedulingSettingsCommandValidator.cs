using FluentValidation;

namespace BookingTracker.Application.Availability.Commands.UpdateBookingPageSchedulingSettings;

public class UpdateBookingPageSchedulingSettingsCommandValidator : AbstractValidator<UpdateBookingPageSchedulingSettingsCommand>
{
    public UpdateBookingPageSchedulingSettingsCommandValidator()
    {
        RuleFor(x => x.DurationMinutes).GreaterThan(0).LessThanOrEqualTo(24 * 60);
        RuleFor(x => x.BufferBeforeMinutes).GreaterThanOrEqualTo(0).LessThanOrEqualTo(24 * 60);
        RuleFor(x => x.BufferAfterMinutes).GreaterThanOrEqualTo(0).LessThanOrEqualTo(24 * 60);
    }
}
