using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.Common.Authorization;

/// <summary>
/// Centralizes the "does this organizer own this booking page" check so every
/// organizer-scoped handler enforces it the same way instead of re-deriving it.
/// </summary>
public static class OwnershipGuard
{
    public static async Task EnsureOrganizerOwnsBookingPageAsync(
        IBookingTrackerDbContext db, Guid bookingPageId, Guid organizerId, CancellationToken cancellationToken)
    {
        var ownerId = await db.BookingPages
            .Where(p => p.Id == bookingPageId)
            .Select(p => (Guid?)p.OrganizerId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(BookingPage), bookingPageId);

        if (ownerId != organizerId)
            throw new ForbiddenException("You do not have access to this booking page.");
    }
}
