using BookingTracker.Application.Availability.Dtos;
using MediatR;

namespace BookingTracker.Application.Availability.Queries.GetAvailableSlots;

/// <summary>Public - anyone can list a booking page's open slots without authenticating.</summary>
public record GetAvailableSlotsQuery(Guid BookingPageId, DateOnly FromDate, DateOnly ToDate)
    : IRequest<IReadOnlyList<AvailableSlotDto>>;
