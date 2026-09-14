using FluentValidation;

namespace BookingTracker.Application.Availability.Queries.GetAvailableSlots;

public class GetAvailableSlotsQueryValidator : AbstractValidator<GetAvailableSlotsQuery>
{
    private const int MaxRangeDays = 62;

    public GetAvailableSlotsQueryValidator()
    {
        RuleFor(x => x).Must(x => x.ToDate >= x.FromDate)
            .WithMessage("ToDate must not be before FromDate.");

        RuleFor(x => x).Must(x => (x.ToDate.ToDateTime(TimeOnly.MinValue) - x.FromDate.ToDateTime(TimeOnly.MinValue)).Days <= MaxRangeDays)
            .WithMessage($"Date range cannot exceed {MaxRangeDays} days.");
    }
}
