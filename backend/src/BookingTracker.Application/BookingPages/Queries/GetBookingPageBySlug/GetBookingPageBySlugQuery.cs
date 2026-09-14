using BookingTracker.Application.BookingPages.Dtos;
using MediatR;

namespace BookingTracker.Application.BookingPages.Queries.GetBookingPageBySlug;

public record GetBookingPageBySlugQuery(string Slug) : IRequest<BookingPageDto>;
