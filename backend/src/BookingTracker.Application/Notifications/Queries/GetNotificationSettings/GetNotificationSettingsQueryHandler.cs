using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using BookingTracker.Application.Notifications.Dtos;
using BookingTracker.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.Notifications.Queries.GetNotificationSettings;

public class GetNotificationSettingsQueryHandler : IRequestHandler<GetNotificationSettingsQuery, NotificationSettingsDto>
{
    private readonly IBookingTrackerDbContext _db;

    public GetNotificationSettingsQueryHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<NotificationSettingsDto> Handle(GetNotificationSettingsQuery request, CancellationToken cancellationToken)
    {
        var settings = await _db.NotificationSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizerId == request.OrganizerId, cancellationToken);

        // No row yet just means "never customized" - the organizer's effective settings are
        // still the defaults (same fallback EmailNotificationService itself uses), not an error.
        return (settings ?? NotificationSettings.CreateDefault(request.OrganizerId)).ToDto();
    }
}
