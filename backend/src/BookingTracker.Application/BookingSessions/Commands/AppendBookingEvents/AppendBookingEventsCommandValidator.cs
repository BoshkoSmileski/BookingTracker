using BookingTracker.Domain.Common;
using BookingTracker.Domain.Enums;
using FluentValidation;

namespace BookingTracker.Application.BookingSessions.Commands.AppendBookingEvents;

public class AppendBookingEventsCommandValidator : AbstractValidator<AppendBookingEventsCommand>
{
    public AppendBookingEventsCommandValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();

        RuleFor(x => x.Events)
            .NotEmpty().WithMessage("At least one event must be supplied.")
            .Must(events => events.Count <= 200).WithMessage("A single batch cannot contain more than 200 events.");

        RuleForEach(x => x.Events).ChildRules(e =>
        {
            e.RuleFor(x => x.EventType).NotEmpty();
            e.RuleFor(x => x.ClientSequenceNumber).GreaterThan(0);

            // Length only, never email format, here: the frontend reports every
            // keystroke as its own event (not just the final value), so a
            // partial in-progress value like "jane@exam" is completely normal
            // mid-typing and must not be rejected. A value that's already too
            // long is never "not finished yet" though, so it's safe (and
            // matches the DB columns via BookingFieldLimits) to reject
            // immediately. Full email format is checked once, at submit time,
            // in BookingSession.Submit - see that method for why.
            e.When(x => Is(x.EventType, BookingEventType.FieldChanged), () =>
            {
                e.RuleFor(x => x.NewValue)
                    .MaximumLength(BookingFieldLimits.NameMaxLength)
                    .When(x => x.FieldName == BookingFieldNames.Name)
                    .WithMessage($"Name cannot exceed {BookingFieldLimits.NameMaxLength} characters.");

                e.RuleFor(x => x.NewValue)
                    .MaximumLength(BookingFieldLimits.EmailMaxLength)
                    .When(x => x.FieldName == BookingFieldNames.Email)
                    .WithMessage($"Email cannot exceed {BookingFieldLimits.EmailMaxLength} characters.");

                e.RuleFor(x => x.NewValue)
                    .MaximumLength(BookingFieldLimits.PhoneMaxLength)
                    .When(x => x.FieldName == BookingFieldNames.Phone)
                    .WithMessage($"Phone cannot exceed {BookingFieldLimits.PhoneMaxLength} characters.");

                e.RuleFor(x => x.NewValue)
                    .MaximumLength(BookingFieldLimits.MessageMaxLength)
                    .When(x => x.FieldName == BookingFieldNames.Message)
                    .WithMessage($"Message cannot exceed {BookingFieldLimits.MessageMaxLength} characters.");

                // An answer to an organizer-defined custom field. Same reasoning
                // as the four above - the column limit, checked before the
                // request reaches a handler. Whether a *required* field was
                // answered at all is page configuration rather than a per-event
                // fact, so it is checked once at submit time instead.
                e.RuleFor(x => x.NewValue)
                    .MaximumLength(BookingFieldLimits.CustomAnswerMaxLength)
                    .When(x => BookingFieldNames.TryGetCustomFieldId(x.FieldName) is not null)
                    .WithMessage($"An answer cannot exceed {BookingFieldLimits.CustomAnswerMaxLength} characters.");
            });

            // Malformed date/time strings previously reached DateOnly.Parse/
            // TimeOnly.Parse in the handler unvalidated, throwing a raw
            // FormatException that surfaced as an opaque 500 instead of a
            // clean 400 - same class of bug as the SQL truncation issue this
            // validator otherwise exists to prevent, just for a different
            // exception type.
            e.RuleFor(x => x.NewValue)
                .Must(v => string.IsNullOrEmpty(v) || DateOnly.TryParse(v, out _))
                .When(x => Is(x.EventType, BookingEventType.DateSelected))
                .WithMessage("Date must be a valid date.");

            e.RuleFor(x => x.NewValue)
                .Must(v => string.IsNullOrEmpty(v) || TimeOnly.TryParse(v, out _))
                .When(x => Is(x.EventType, BookingEventType.TimeSelected))
                .WithMessage("Time must be a valid time.");
        });
    }

    /// <summary>
    /// Recognises an event type exactly as
    /// <c>AppendBookingEventsCommandHandler</c> does - by parsing it, case
    /// insensitively - rather than by string equality against the enum's name.
    ///
    /// The two must agree, and comparing strings meant they did not. The
    /// handler accepts "dateselected", so a lower-cased spelling reached
    /// <c>DateOnly.Parse</c> while every <c>When</c> here evaluated false and
    /// skipped the rule that exists to stop precisely that - turning a clean
    /// 400 back into the unhandled FormatException and opaque 500 this
    /// validator was written to remove. Measured: "DateSelected" with a
    /// malformed value answered 400, "dateselected" with the same value
    /// answered 500.
    ///
    /// The length rules were bypassable the same way and were saved by
    /// BookingSession's own invariants, which is what the two
    /// enforcement points are for - but the date/time rules have no such
    /// second line, because parsing happens in the handler before the domain
    /// is reached at all.
    /// </summary>
    private static bool Is(string? eventType, BookingEventType expected)
        => Enum.TryParse<BookingEventType>(eventType, ignoreCase: true, out var parsed) && parsed == expected;
}
