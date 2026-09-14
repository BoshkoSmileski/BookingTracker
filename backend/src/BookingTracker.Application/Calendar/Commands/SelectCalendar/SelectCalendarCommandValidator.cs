using FluentValidation;

namespace BookingTracker.Application.Calendar.Commands.SelectCalendar;

public class SelectCalendarCommandValidator : AbstractValidator<SelectCalendarCommand>
{
    public SelectCalendarCommandValidator()
    {
        RuleFor(x => x.ExternalCalendarId).NotEmpty();
        RuleFor(x => x.ExternalCalendarName).NotEmpty();
    }
}
