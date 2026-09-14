using BookingTracker.Application.BookingPages.Dtos;
using BookingTracker.Domain.Enums;
using MediatR;

namespace BookingTracker.Application.BookingPages.Commands.BookingFormFields.AddBookingFormField;

/// <summary>
/// Adds one organizer-defined question to a booking page's form - the thing a
/// visitor answers, as opposed to an AddBookingInstructionCommand, which adds
/// something they only read.
///
/// DisplayOrder is deliberately not a parameter: BookingPage.AddFormField
/// derives it, so two callers can never claim the same position.
/// </summary>
public record AddBookingFormFieldCommand(
    Guid OrganizerId,
    Guid BookingPageId,
    string Label,
    BookingFieldType Type,
    bool IsRequired) : IRequest<BookingPageDetailDto>;
