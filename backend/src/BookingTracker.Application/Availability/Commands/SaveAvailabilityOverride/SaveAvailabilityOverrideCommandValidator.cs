using BookingTracker.Domain.Entities;
using FluentValidation;

namespace BookingTracker.Application.Availability.Commands.SaveAvailabilityOverride;

public class SaveAvailabilityOverrideCommandValidator : AbstractValidator<SaveAvailabilityOverrideCommand>
{
    public SaveAvailabilityOverrideCommandValidator()
    {
        // Sourced from the entity's own constant rather than a second copy of the
        // number, the same way BookingFieldLimits is the one source for text
        // limits. The domain enforces it too - this is the fast-fail boundary
        // that turns it into the project's standard 400 instead of a 500.
        RuleFor(x => x.Ranges)
            .NotNull()
            .Must(r => r.Count <= AvailabilityOverride.MaxRanges)
            .WithMessage($"A date-specific schedule cannot have more than {AvailabilityOverride.MaxRanges} time ranges.");

        RuleForEach(x => x.Ranges)
            .Must(r => r.Start < r.End)
            .WithMessage("Each time range must start before it ends.");

        // Overlap is rejected rather than merged, matching the weekly schedule:
        // two overlapping open ranges would produce the same slot twice.
        RuleFor(x => x.Ranges)
            .Must(ranges =>
            {
                var ordered = ranges.OrderBy(r => r.Start).ToList();
                for (var i = 1; i < ordered.Count; i++)
                {
                    if (ordered[i].Start < ordered[i - 1].End) return false;
                }
                return true;
            })
            .WithMessage("Time ranges must not overlap.")
            .When(x => x.Ranges is { Count: > 1 });

        RuleFor(x => x.Note).MaximumLength(200);
    }
}
