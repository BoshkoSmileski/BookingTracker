using FluentValidation;

namespace BookingTracker.Application.BookingPages.Commands.CreateBookingPage;

public class CreateBookingPageCommandValidator : AbstractValidator<CreateBookingPageCommand>
{
    public CreateBookingPageCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.DurationMinutes).GreaterThan(0).LessThanOrEqualTo(24 * 60);
        RuleFor(x => x.BufferBeforeMinutes).GreaterThanOrEqualTo(0).LessThanOrEqualTo(24 * 60);
        RuleFor(x => x.BufferAfterMinutes).GreaterThanOrEqualTo(0).LessThanOrEqualTo(24 * 60);
        RuleFor(x => x.MinNoticeMinutes).GreaterThanOrEqualTo(0).When(x => x.MinNoticeMinutes.HasValue);
        RuleFor(x => x.MaxBookingWindowDays).InclusiveBetween(1, 365).When(x => x.MaxBookingWindowDays.HasValue);
        RuleFor(x => x.MaxBookingsPerDay).GreaterThanOrEqualTo(1).When(x => x.MaxBookingsPerDay.HasValue);
    }
}
