using BookingTracker.Application.Common.Authorization;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using BookingTracker.Application.Notifications.Dtos;
using BookingTracker.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.Notifications.Queries.GetBookingReminders;

public class GetBookingRemindersQueryHandler : IRequestHandler<GetBookingRemindersQuery, IReadOnlyList<BookingReminderDto>>
{
    private readonly IBookingTrackerDbContext _db;

    public GetBookingRemindersQueryHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<IReadOnlyList<BookingReminderDto>> Handle(GetBookingRemindersQuery request, CancellationToken cancellationToken)
    {
        var session = await _db.BookingSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.SessionId, cancellationToken)
            ?? throw new NotFoundException(nameof(BookingSession), request.SessionId);

        await OwnershipGuard.EnsureOrganizerOwnsBookingPageAsync(_db, session.BookingPageId, request.RequestingOrganizerId, cancellationToken);

        var reminders = await _db.BookingReminders.AsNoTracking()
            .Where(r => r.BookingSessionId == request.SessionId)
            .OrderByDescending(r => r.MinutesBeforeEvent)
            .ToListAsync(cancellationToken);

        if (reminders.Count == 0) return [];

        // One extra query for every linked notification, not one per reminder - the
        // delivery half of each row is fetched in a single batched lookup.
        var notificationIds = reminders
            .Where(r => r.EmailNotificationId is not null)
            .Select(r => r.EmailNotificationId!.Value)
            .ToList();

        var notificationById = notificationIds.Count == 0
            ? []
            : await _db.EmailNotifications.AsNoTracking()
                .Where(n => notificationIds.Contains(n.Id))
                .ToDictionaryAsync(n => n.Id, cancellationToken);

        return reminders
            .Select(r => r.ToDto(r.EmailNotificationId is { } id ? notificationById.GetValueOrDefault(id) : null))
            .ToList();
    }
}
