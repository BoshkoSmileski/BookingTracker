using BookingTracker.Application.BookingPages.Dtos;
using MediatR;

namespace BookingTracker.Application.BookingPages.Queries.GetBookingPageById;

public record GetBookingPageByIdQuery(Guid BookingPageId, Guid OrganizerId) : IRequest<BookingPageDetailDto>;
