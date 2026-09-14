using FluentValidation;

namespace BookingTracker.Application.Bookings.Commands.RescheduleBooking;

public class RescheduleBookingCommandValidator : AbstractValidator<RescheduleBookingCommand>
{
    public RescheduleBookingCommandValidator()
    {
        RuleFor(x => x)
            .Must(x => x.SessionId.HasValue ^ !string.IsNullOrEmpty(x.PublicToken))
            .WithMessage("Exactly one of SessionId or PublicToken must be provided.");
    }
}
