using BookingTracker.Application.Availability.Dtos;
using MediatR;

namespace BookingTracker.Application.Availability.Queries.GetAvailabilityOverrides;

/// <param name="From">Inclusive lower bound. Null means "from the earliest override".</param>
/// <param name="To">Inclusive upper bound. Null means "to the last override".</param>
public record GetAvailabilityOverridesQuery(
    Guid OrganizerId,
    DateOnly? From = null,
    DateOnly? To = null) : IRequest<IReadOnlyList<AvailabilityOverrideDto>>;
