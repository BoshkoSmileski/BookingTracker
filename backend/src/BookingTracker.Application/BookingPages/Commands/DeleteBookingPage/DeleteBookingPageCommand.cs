using MediatR;

namespace BookingTracker.Application.BookingPages.Commands.DeleteBookingPage;

public record DeleteBookingPageCommand(Guid OrganizerId, Guid BookingPageId) : IRequest;
