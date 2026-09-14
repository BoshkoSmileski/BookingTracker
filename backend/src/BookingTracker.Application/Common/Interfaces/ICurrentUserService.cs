namespace BookingTracker.Application.Common.Interfaces;

/// <summary>
/// Exposes the authenticated organizer's id to Application handlers without
/// Application taking a dependency on HttpContext. Implemented in the Api
/// layer (the only layer allowed to know about HTTP) by reading JWT claims.
/// </summary>
public interface ICurrentUserService
{
    Guid? OrganizerId { get; }
}
