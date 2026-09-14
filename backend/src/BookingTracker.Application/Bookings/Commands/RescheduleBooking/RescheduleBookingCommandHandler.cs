using BookingTracker.Application.Bookings.Dtos;
using BookingTracker.Application.Common;
using BookingTracker.Application.Common.Authorization;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BookingTracker.Application.Bookings.Commands.RescheduleBooking;

public class RescheduleBookingCommandHandler : IRequestHandler<RescheduleBookingCommand, BookingConfirmationDto>
{
    private readonly IBookingTrackerDbContext _db;
    private readonly ISender _sender;
    private readonly IEventBroadcaster _broadcaster;
    private readonly IEmailNotificationService _emailNotificationService;
    private readonly IBookingReminderScheduler _reminderScheduler;
    private readonly ICalendarSyncService _calendarSyncService;
    private readonly ILogger<RescheduleBookingCommandHandler> _logger;

    public RescheduleBookingCommandHandler(
        IBookingTrackerDbContext db, ISender sender, IEventBroadcaster broadcaster, IEmailNotificationService emailNotificationService,
        IBookingReminderScheduler reminderScheduler, ICalendarSyncService calendarSyncService, ILogger<RescheduleBookingCommandHandler> logger)
    {
        _db = db;
        _sender = sender;
        _broadcaster = broadcaster;
        _emailNotificationService = emailNotificationService;
        _reminderScheduler = reminderScheduler;
        _calendarSyncService = calendarSyncService;
        _logger = logger;
    }

    public async Task<BookingConfirmationDto> Handle(RescheduleBookingCommand request, CancellationToken cancellationToken)
    {
        var session = await ResolveSessionAsync(request, cancellationToken);

        if (request.RequestingOrganizerId is { } organizerId)
        {
            await OwnershipGuard.EnsureOrganizerOwnsBookingPageAsync(_db, session.BookingPageId, organizerId, cancellationToken);
        }

        var page = await _db.BookingPages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == session.BookingPageId, cancellationToken)
            ?? throw new NotFoundException(nameof(BookingPage), session.BookingPageId);

        await EnsureNotInThePastAsync(page.OrganizerId, request.NewDate, request.NewTime, cancellationToken);

        // A guest holding a PublicToken may only move a booking to a slot the
        // page is actually offering. Without this the public reschedule endpoint
        // was the same hole as submit, one screen further on: measured over
        // HTTP, a token holder moved a confirmed booking onto a closed Saturday
        // and then to 03:00 on a 09:00-17:00 page, both answered 200.
        //
        // Deliberately NOT applied to the organizer's own path. They are
        // authenticated, they own the calendar, and placing a booking outside
        // their published hours is a thing an organizer may legitimately want
        // to do - the public hours describe what strangers may take, not what
        // its owner may schedule. Narrowing that would be a product change
        // rather than a security fix, and this is a security fix.
        if (request.RequestingOrganizerId is null)
        {
            await BookableSlotGuard.EnsureSlotIsOfferedAsync(
                _sender, session.BookingPageId, request.NewDate, request.NewTime, cancellationToken);
        }

        var oldDate = session.SelectedDate;
        var oldTime = session.SelectedTime;
        var context = new ClientContext(request.ClientIp, request.UserAgent);
        BookingSessionEvent? rescheduledEvent = null;

        // Same claim protocol as a first-time submit: the slot is checked and taken
        // inside one serializable transaction, and a race lost to a concurrent
        // claim is reported as the slot conflict it is rather than as a failure.
        await BookingConflictChecker.ClaimSlotAsync(
            _db, session.Id, session.BookingPageId, request.NewDate, request.NewTime,
            async () =>
            {
                rescheduledEvent = session.Reschedule(request.NewDate, request.NewTime, context);
                _db.BookingSessionEvents.Add(rescheduledEvent);
                await _db.SaveChangesAsync(cancellationToken);
            },
            cancellationToken);

        await _broadcaster.BroadcastEventAsync(rescheduledEvent!.ToDto(), cancellationToken);
        await _broadcaster.BroadcastSessionUpdatedAsync(session.ToDto(), cancellationToken);

        // Before the emails, for the same reason as in
        // SubmitBookingSessionCommandHandler: the reschedule notice and its ICS
        // attachment are rendered from the session's stored meeting link, and
        // this is the call that can supply one to a booking that did not have
        // an event before. A booking that already has a meeting keeps the same
        // one - the calendar event is updated in place and its conference is
        // preserved - so for the common path this reordering changes nothing
        // except that both emails are now guaranteed to see the same state.
        try
        {
            _logger.LogInformation("Syncing rescheduled booking to calendar for session {SessionId}.", session.Id);
            await _calendarSyncService.SyncBookingRescheduledAsync(session.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync calendar event for rescheduled session {SessionId}.", session.Id);
        }

        // Reported on the response so the guest's reschedule screen only claims
        // a confirmation is on its way when one was really queued. False if this
        // throws: queueing is best-effort and never fails the reschedule.
        var guestConfirmationQueued = false;
        try
        {
            guestConfirmationQueued = await _emailNotificationService.QueueBookingRescheduledAsync(session, oldDate, oldTime, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue reschedule confirmation emails for session {SessionId}.", session.Id);
        }

        // Reminders still point at the old time - drop the pending ones and rebuild
        // against the new one. Reminders already handed to the mailer are left as
        // history; that mail is out and cannot be recalled.
        try
        {
            await _reminderScheduler.RescheduleForBookingAsync(session, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to regenerate reminders for rescheduled session {SessionId}.", session.Id);
        }

        return session.ToConfirmationDto(guestConfirmationQueued);
    }

    private async Task<BookingSession> ResolveSessionAsync(RescheduleBookingCommand request, CancellationToken cancellationToken)
    {
        if (request.SessionId is { } sessionId)
        {
            return await _db.BookingSessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
                ?? throw new NotFoundException(nameof(BookingSession), sessionId);
        }

        return await _db.BookingSessions.FirstOrDefaultAsync(s => s.PublicToken == request.PublicToken, cancellationToken)
            ?? throw new NotFoundException(nameof(BookingSession), request.PublicToken!);
    }

    private async Task EnsureNotInThePastAsync(Guid organizerId, DateOnly date, TimeOnly time, CancellationToken cancellationToken)
    {
        var timeZoneId = await _db.WorkingSchedules.AsNoTracking()
            .Where(s => s.OrganizerId == organizerId)
            .Select(s => s.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken);

        var newSlotUtc = BookingScheduleTime.ToUtc(date, time, timeZoneId);

        if (newSlotUtc <= DateTime.UtcNow)
            throw new ConflictException("Cannot reschedule to a time in the past.");
    }
}
