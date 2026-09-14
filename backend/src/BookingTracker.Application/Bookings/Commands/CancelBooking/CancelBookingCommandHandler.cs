using BookingTracker.Application.Bookings.Dtos;
using BookingTracker.Application.Common.Authorization;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookingTracker.Application.Bookings.Commands.CancelBooking;

public class CancelBookingCommandHandler : IRequestHandler<CancelBookingCommand, BookingConfirmationDto>
{
    private readonly IBookingTrackerDbContext _db;
    private readonly IEventBroadcaster _broadcaster;
    private readonly IEmailNotificationService _emailNotificationService;
    private readonly IBookingReminderScheduler _reminderScheduler;
    private readonly ICalendarSyncService _calendarSyncService;
    private readonly ILogger<CancelBookingCommandHandler> _logger;

    public CancelBookingCommandHandler(
        IBookingTrackerDbContext db, IEventBroadcaster broadcaster, IEmailNotificationService emailNotificationService,
        IBookingReminderScheduler reminderScheduler, ICalendarSyncService calendarSyncService, ILogger<CancelBookingCommandHandler> logger)
    {
        _db = db;
        _broadcaster = broadcaster;
        _emailNotificationService = emailNotificationService;
        _reminderScheduler = reminderScheduler;
        _calendarSyncService = calendarSyncService;
        _logger = logger;
    }

    public async Task<BookingConfirmationDto> Handle(CancelBookingCommand request, CancellationToken cancellationToken)
    {
        var session = await ResolveSessionAsync(request, cancellationToken);

        if (request.RequestingOrganizerId is { } organizerId)
        {
            await OwnershipGuard.EnsureOrganizerOwnsBookingPageAsync(_db, session.BookingPageId, organizerId, cancellationToken);
        }

        var context = new ClientContext(request.ClientIp, request.UserAgent);
        var cancelledEvent = session.Cancel(request.CancelledBy, request.Reason, context);
        _db.BookingSessionEvents.Add(cancelledEvent);
        await _db.SaveChangesAsync(cancellationToken);

        await _broadcaster.BroadcastEventAsync(cancelledEvent.ToDto(), cancellationToken);
        await _broadcaster.BroadcastSessionUpdatedAsync(session.ToDto(), cancellationToken);

        // Reported on the response so the guest's cancel screen only claims a
        // confirmation is on its way when one was really queued. False if this
        // throws: queueing is best-effort and never fails the cancellation.
        var guestConfirmationQueued = false;
        try
        {
            guestConfirmationQueued = await _emailNotificationService.QueueBookingCancelledAsync(session, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue cancellation emails for session {SessionId}.", session.Id);
        }

        // A cancelled booking must never remind anyone about a meeting that is no
        // longer happening - drop every reminder that has not already been handed
        // to the mailer.
        try
        {
            await _reminderScheduler.CancelForBookingAsync(session, "Booking cancelled", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel pending reminders for session {SessionId}.", session.Id);
        }

        try
        {
            _logger.LogInformation("Syncing cancelled booking to calendar for session {SessionId}.", session.Id);
            await _calendarSyncService.SyncBookingCancelledAsync(session.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove calendar event for cancelled session {SessionId}.", session.Id);
        }

        return session.ToConfirmationDto(guestConfirmationQueued);
    }

    private async Task<BookingSession> ResolveSessionAsync(CancelBookingCommand request, CancellationToken cancellationToken)
    {
        if (request.SessionId is { } sessionId)
        {
            return await _db.BookingSessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
                ?? throw new NotFoundException(nameof(BookingSession), sessionId);
        }

        return await _db.BookingSessions.FirstOrDefaultAsync(s => s.PublicToken == request.PublicToken, cancellationToken)
            ?? throw new NotFoundException(nameof(BookingSession), request.PublicToken!);
    }
}
