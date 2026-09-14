using MediatR;

namespace BookingTracker.Application.Bookings.Commands.ResendConfirmation;

/// <summary>Organizer-only - resends the original booking confirmation email to the customer.</summary>
public record ResendConfirmationCommand(Guid SessionId, Guid RequestingOrganizerId) : IRequest;
