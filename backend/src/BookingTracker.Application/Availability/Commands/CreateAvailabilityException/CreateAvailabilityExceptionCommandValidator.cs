using BookingTracker.Domain.Enums;
using FluentValidation;

namespace BookingTracker.Application.Availability.Commands.CreateAvailabilityException;

public class CreateAvailabilityExceptionCommandValidator : AbstractValidator<CreateAvailabilityExceptionCommand>
{
    /// <summary>Roughly a year - long enough for any real sabbatical, short enough that a mistyped year is caught.</summary>
    public const int MaxRangeDays = 366;

    public CreateAvailabilityExceptionCommandValidator()
    {
        RuleFor(x => x.Type)
            .NotEmpty()
            .Must(t => Enum.TryParse<AvailabilityExceptionType>(t, ignoreCase: true, out _))
            .WithMessage("Type must be one of: " + string.Join(", ", Enum.GetNames<AvailabilityExceptionType>()));

        RuleFor(x => x)
            .Must(x => x.StartTime.HasValue == x.EndTime.HasValue)
            .WithMessage("StartTime and EndTime must both be provided, or both omitted for a whole-day exception.");

        // Guarded on BOTH, not just StartTime. FluentValidation evaluates every
        // RuleFor chain, so the rule above failing does not stop this one from
        // running - and with only StartTime supplied, x.EndTime.Value threw
        // "Nullable object must have a value" out of the validator itself. That
        // is a 500 for exactly the input the rule above exists to answer with a
        // 400, and it made that rule's message unreachable. Same shape as the
        // handler/validator casing mismatch fixed alongside it.
        RuleFor(x => x)
            .Must(x => x.StartTime!.Value < x.EndTime!.Value)
            .WithMessage("StartTime must be before EndTime.")
            .When(x => x.StartTime.HasValue && x.EndTime.HasValue);

        RuleFor(x => x)
            .Must(x => x.EndDate is not { } end || end >= x.Date)
            .WithMessage("The end date must not be before the start date.")
            .When(x => x.EndDate.HasValue);

        // A blocked range is stored as one row, so length costs nothing to keep -
        // but a mistyped year would silently block availability for centuries,
        // and that is far harder to notice than a rejected form. Policy lives
        // here; the domain only enforces end >= start, which is a true invariant.
        RuleFor(x => x)
            .Must(x => x.EndDate is not { } end || end.DayNumber - x.Date.DayNumber + 1 <= MaxRangeDays)
            .WithMessage($"A blocked period cannot be longer than {MaxRangeDays} days.")
            .When(x => x.EndDate.HasValue);

        RuleFor(x => x.Reason).MaximumLength(500);
    }
}
