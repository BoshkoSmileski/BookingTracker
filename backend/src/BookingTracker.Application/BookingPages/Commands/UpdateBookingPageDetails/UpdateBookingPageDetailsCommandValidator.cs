using FluentValidation;

namespace BookingTracker.Application.BookingPages.Commands.UpdateBookingPageDetails;

public class UpdateBookingPageDetailsCommandValidator : AbstractValidator<UpdateBookingPageDetailsCommand>
{
    public UpdateBookingPageDetailsCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
    }
}
