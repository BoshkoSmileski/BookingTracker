using BookingTracker.Application.Common.Authorization;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BookingTracker.Infrastructure.RealTime;

/// <summary>
/// Clients (organizer dashboards) join a group per booking page they're
/// viewing, so a page's live event stream only reaches dashboards actually
/// watching that page. Requires the same JWT bearer auth as the REST API
/// (anonymous connections are rejected at the handshake), and re-validates
/// booking page ownership per join via the same OwnershipGuard every
/// organizer-scoped command/query handler already uses - the client-supplied
/// bookingPageId is never trusted on its own.
/// </summary>
[Authorize]
public class OrganizerDashboardHub : Hub
{
    private readonly IBookingTrackerDbContext _db;

    public OrganizerDashboardHub(IBookingTrackerDbContext db)
    {
        _db = db;
    }

    public async Task JoinBookingPageGroup(Guid bookingPageId)
    {
        var organizerId = Context.User.GetOrganizerId()
            ?? throw new HubException("Not authenticated.");

        try
        {
            await OwnershipGuard.EnsureOrganizerOwnsBookingPageAsync(
                _db, bookingPageId, organizerId, Context.ConnectionAborted);
        }
        catch (NotFoundException)
        {
            // Mirrors the REST API's 404 for the same case (see OwnershipGuard).
            throw new HubException("Booking page not found.");
        }
        catch (ForbiddenException)
        {
            // Mirrors the REST API's 403.
            throw new HubException("You do not have access to this booking page.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(bookingPageId));
    }

    public Task LeaveBookingPageGroup(Guid bookingPageId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(bookingPageId));

    public static string GroupName(Guid bookingPageId) => $"booking-page-{bookingPageId}";
}
