using BookingTracker.Application.BookingPages.Dtos;
using MediatR;

namespace BookingTracker.Application.BookingPages.Commands.SetBookingPageActive;

public record SetBookingPageActiveCommand(Guid OrganizerId, Guid BookingPageId, bool IsActive) : IRequest<BookingPageDetailDto>;
