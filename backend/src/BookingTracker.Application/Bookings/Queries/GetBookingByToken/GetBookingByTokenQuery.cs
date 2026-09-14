using BookingTracker.Application.Bookings.Dtos;
using MediatR;

namespace BookingTracker.Application.Bookings.Queries.GetBookingByToken;

public record GetBookingByTokenQuery(string PublicToken) : IRequest<PublicBookingDto>;
