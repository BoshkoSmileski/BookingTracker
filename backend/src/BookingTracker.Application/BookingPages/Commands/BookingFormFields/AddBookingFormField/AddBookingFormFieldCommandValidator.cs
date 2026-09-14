using BookingTracker.Domain.Common;
using FluentValidation;

namespace BookingTracker.Application.BookingPages.Commands.BookingFormFields.AddBookingFormField;

public class AddBookingFormFieldCommandValidator : AbstractValidator<AddBookingFormFieldCommand>
{
    public AddBookingFormFieldCommandValidator()
    {
        RuleFor(x => x.Label)
            .NotEmpty().WithMessage("A field label is required.")
            .MaximumLength(BookingFieldLimits.CustomFieldLabelMaxLength)
            .WithMessage($"A field label cannot exceed {BookingFieldLimits.CustomFieldLabelMaxLength} characters.");

        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Field type must be either ShortText or LongText.");
    }
}
