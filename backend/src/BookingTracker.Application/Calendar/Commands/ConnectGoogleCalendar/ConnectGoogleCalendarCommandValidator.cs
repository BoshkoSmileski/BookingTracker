using FluentValidation;

namespace BookingTracker.Application.Calendar.Commands.ConnectGoogleCalendar;

public class ConnectGoogleCalendarCommandValidator : AbstractValidator<ConnectGoogleCalendarCommand>
{
    public ConnectGoogleCalendarCommandValidator()
    {
        RuleFor(x => x.AuthorizationCode).NotEmpty();
    }
}
