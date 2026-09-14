using System.Security.Claims;

namespace BookingTracker.Infrastructure.Auth;

/// <summary>
/// Single place that knows how the organizer id is encoded in a JWT-derived
/// ClaimsPrincipal, shared by <see cref="BookingTracker.Api.Common.CurrentUserService"/>
/// (HTTP requests) and <see cref="RealTime.OrganizerDashboardHub"/> (SignalR
/// connections) so the two never drift apart.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static Guid? GetOrganizerId(this ClaimsPrincipal? principal)
    {
        var value = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal?.FindFirstValue("sub");
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
