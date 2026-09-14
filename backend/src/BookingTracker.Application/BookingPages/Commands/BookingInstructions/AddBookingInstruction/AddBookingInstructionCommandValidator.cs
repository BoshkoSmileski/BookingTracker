using FluentValidation;

namespace BookingTracker.Application.BookingPages.Commands.BookingInstructions.AddBookingInstruction;

public class AddBookingInstructionCommandValidator : AbstractValidator<AddBookingInstructionCommand>
{
    public AddBookingInstructionCommandValidator()
    {
        RuleFor(x => x.Text)
            .NotEmpty().WithMessage("Instruction text is required.")
            .MaximumLength(300).WithMessage("An instruction cannot be longer than 300 characters.");
    }
}
