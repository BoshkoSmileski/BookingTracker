using FluentValidation;

namespace BookingTracker.Application.BookingPages.Commands.UpdateBookingPageLimits;

public class UpdateBookingPageLimitsCommandValidator : AbstractValidator<UpdateBookingPageLimitsCommand>
{
    public UpdateBookingPageLimitsCommandValidator()
    {
        RuleFor(x => x.MinNoticeMinutes).GreaterThanOrEqualTo(0).When(x => x.MinNoticeMinutes.HasValue);
        RuleFor(x => x.MaxBookingWindowDays).InclusiveBetween(1, 365).When(x => x.MaxBookingWindowDays.HasValue);
        RuleFor(x => x.MaxBookingsPerDay).GreaterThanOrEqualTo(1).When(x => x.MaxBookingsPerDay.HasValue);
    }
}
