using MediatR;

namespace BookingTracker.Application.Availability.Commands.DeleteAvailabilityException;

/// <summary>OrganizerId scopes the delete so an organizer can never delete another organizer's exception by guessing an id.</summary>
public record DeleteAvailabilityExceptionCommand(Guid OrganizerId, Guid ExceptionId) : IRequest;
