using BookingTracker.Application.BookingPages.Dtos;
using MediatR;

namespace BookingTracker.Application.BookingPages.Commands.BookingFormFields.RemoveBookingFormField;

/// <summary>
/// Stops a booking page asking a question. Answers already given to it are left
/// alone - see BookingPage.MaxFormFields for why removing a field must not
/// rewrite bookings that were made while it existed.
/// </summary>
public record RemoveBookingFormFieldCommand(Guid OrganizerId, Guid BookingPageId, Guid FieldId) : IRequest<BookingPageDetailDto>;
