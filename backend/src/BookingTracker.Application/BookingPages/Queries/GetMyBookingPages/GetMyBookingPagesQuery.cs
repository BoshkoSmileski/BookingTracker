using BookingTracker.Application.BookingPages.Dtos;
using MediatR;

namespace BookingTracker.Application.BookingPages.Queries.GetMyBookingPages;

public record GetMyBookingPagesQuery(Guid OrganizerId) : IRequest<IReadOnlyList<BookingPageSummaryDto>>;
