using BookingTracker.Application.Availability.Dtos;
using MediatR;

namespace BookingTracker.Application.Availability.Queries.GetAvailabilityExceptions;

public record GetAvailabilityExceptionsQuery(Guid OrganizerId, DateOnly? From, DateOnly? To)
    : IRequest<IReadOnlyList<AvailabilityExceptionDto>>;
