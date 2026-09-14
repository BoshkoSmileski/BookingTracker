using BookingTracker.Application.BookingPages.Dtos;
using MediatR;

namespace BookingTracker.Application.BookingPages.Commands.UpdateBookingPageDetails;

public record UpdateBookingPageDetailsCommand(
    Guid OrganizerId,
    Guid BookingPageId,
    string Title,
    string? Description) : IRequest<BookingPageDetailDto>;
