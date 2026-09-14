using BookingTracker.Domain.Common;
using FluentValidation;

namespace BookingTracker.Application.Bookings.Commands.CancelBooking;

public class CancelBookingCommandValidator : AbstractValidator<CancelBookingCommand>
{
    public CancelBookingCommandValidator()
    {
        RuleFor(x => x)
            .Must(x => x.SessionId.HasValue ^ !string.IsNullOrEmpty(x.PublicToken))
            .WithMessage("Exactly one of SessionId or PublicToken must be provided.");

        RuleFor(x => x.Reason).MaximumLength(BookingFieldLimits.CancellationReasonMaxLength);
    }
}
