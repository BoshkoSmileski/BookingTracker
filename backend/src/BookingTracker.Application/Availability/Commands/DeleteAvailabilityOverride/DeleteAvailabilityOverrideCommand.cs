using MediatR;

namespace BookingTracker.Application.Availability.Commands.DeleteAvailabilityOverride;

/// <summary>
/// Removes a date's specific hours, so the date falls back to the weekly
/// schedule. Deliberately not the same as saving an override with no ranges -
/// that is an explicit "closed on this date", which is a different statement
/// and one an organizer needs to be able to make.
/// </summary>
public record DeleteAvailabilityOverrideCommand(Guid OrganizerId, Guid OverrideId) : IRequest;
