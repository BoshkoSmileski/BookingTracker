using BookingTracker.Application.Bookings.Dtos;
using BookingTracker.Application.Common;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using BookingTracker.Domain.Common;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.Domain.ValueObjects;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ValidationException = BookingTracker.Application.Common.Exceptions.ValidationException;

namespace BookingTracker.Application.BookingSessions.Commands.SubmitBookingSession;

public class SubmitBookingSessionCommandHandler : IRequestHandler<SubmitBookingSessionCommand, BookingConfirmationDto>
{
    private readonly IBookingTrackerDbContext _db;
    private readonly ISender _sender;
    private readonly IEventBroadcaster _broadcaster;
    private readonly IEmailNotificationService _emailNotificationService;
    private readonly IBookingReminderScheduler _reminderScheduler;
    private readonly ICalendarSyncService _calendarSyncService;
    private readonly ILogger<SubmitBookingSessionCommandHandler> _logger;

    public SubmitBookingSessionCommandHandler(
        IBookingTrackerDbContext db,
        ISender sender,
        IEventBroadcaster broadcaster,
        IEmailNotificationService emailNotificationService,
        IBookingReminderScheduler reminderScheduler,
        ICalendarSyncService calendarSyncService,
        ILogger<SubmitBookingSessionCommandHandler> logger)
    {
        _db = db;
        _sender = sender;
        _broadcaster = broadcaster;
        _emailNotificationService = emailNotificationService;
        _reminderScheduler = reminderScheduler;
        _calendarSyncService = calendarSyncService;
        _logger = logger;
    }

    public async Task<BookingConfirmationDto> Handle(SubmitBookingSessionCommand request, CancellationToken cancellationToken)
    {
        var session = await _db.BookingSessions
            .FirstOrDefaultAsync(s => s.Id == request.SessionId, cancellationToken)
            ?? throw new NotFoundException(nameof(BookingSession), request.SessionId);

        await EnsureRequiredAnswersGivenAsync(session, cancellationToken);

        // The slot has to be one the page actually offers, not merely one that
        // collides with nothing. BookingConflictChecker below answers only the
        // collision question, so until this guard existed a caller constructing
        // requests by hand could book a closed Saturday, 03:00, a blocked date,
        // a date in the past or past a per-day cap - every rule GET /slots
        // applies was enforced only by clients that chose to ask it. See
        // BookableSlotGuard for why this dispatches the query rather than
        // re-deriving a single rule, and why it sits outside the transaction.
        //
        // Skipped when the session has no slot yet: session.Submit() is the
        // authority on that and answers 400 with the message the wizard shows,
        // which is a better answer than "no such slot is offered".
        //
        // Skipped for a session that is no longer Active for a sharper reason.
        // An already-Submitted booking occupies its own slot, so the guard would
        // report 409 "choose another time" for a double-clicked Submit - sending
        // a guest back to the calendar to redo a booking that had in fact
        // succeeded. EnsureActive inside the claim is the right authority there
        // and answers 400, which is what it did before this guard existed.
        if (session.Status == BookingSessionStatus.Active
            && session.SelectedDate is { } date && session.SelectedTime is { } time)
        {
            await BookableSlotGuard.EnsureSlotIsOfferedAsync(
                _sender, session.BookingPageId, date, time, cancellationToken);
        }

        var context = new ClientContext(request.ClientIp, request.UserAgent);
        BookingSessionEvent? submittedEvent = null;

        // Serializable isolation so two visitors racing to submit the same slot can't
        // both pass the conflict check before either one's write becomes visible to
        // the other. Exactly one of them commits; the other is refused with a
        // ConflictException, whether it lost by seeing the winner's booking or by
        // being the deadlock victim SQL Server chose to enforce that - see
        // BookingConflictChecker.ClaimSlotAsync for why those are the same answer.
        await BookingConflictChecker.ClaimSlotAsync(
            _db, session.Id, session.BookingPageId, session.SelectedDate, session.SelectedTime,
            async () =>
            {
                submittedEvent = session.Submit(request.ClientSequenceNumber, context);
                _db.BookingSessionEvents.Add(submittedEvent);
                await _db.SaveChangesAsync(cancellationToken);
            },
            cancellationToken);

        await _broadcaster.BroadcastEventAsync(submittedEvent!.ToDto(), cancellationToken);
        await _broadcaster.BroadcastSessionUpdatedAsync(session.ToDto(), cancellationToken);

        // Calendar sync runs FIRST of the three best-effort blocks, and the
        // order is load-bearing rather than incidental.
        //
        // A Google Meet link is created by Google as part of inserting the
        // calendar event, and is stored on the session by this call. The
        // confirmation email and its ICS attachment are both rendered at
        // compose time from that stored value, so queueing them before this ran
        // would send every guest a confirmation with no Join Meeting link -
        // and the queue row is immutable once written, so the link could never
        // catch up. It also puts the URL on the DTO returned below, which is
        // what lets the wizard's success step offer the meeting immediately.
        //
        // Nothing about the resilience contract changes: this is still after
        // the booking is durably committed, still in its own try/catch (kept
        // separate from email's so the two failure modes are never conflated in
        // logs), and still unable to fail the request. A Google outage means a
        // booking with no meeting link, never a lost booking - the emails below
        // simply omit their Join sections.
        try
        {
            _logger.LogInformation("Syncing new booking to calendar for session {SessionId}.", session.Id);
            await _calendarSyncService.SyncBookingCreatedAsync(session.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync calendar event for session {SessionId}.", session.Id);
        }

        // Queueing is best-effort: a booking that's already been recorded and
        // committed must never be rolled back or fail the request just because
        // writing the outbound EmailNotification row fails. Actually sending the
        // email happens later, out of the request thread entirely - see
        // EmailQueueProcessor.
        //
        // What comes back is carried onto the response so the wizard's success
        // step can promise a confirmation email only when one was actually
        // written. It stays false if this throws, which is the honest answer -
        // nothing was queued.
        var guestConfirmationQueued = false;
        try
        {
            guestConfirmationQueued = await _emailNotificationService.QueueBookingConfirmedAsync(session, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue booking confirmation emails for session {SessionId}.", session.Id);
        }

        // Reminders are materialized now rather than discovered later by scanning
        // bookings, so they can be shown, cancelled, and regenerated. Its own
        // try/catch for the same reason email and calendar have separate ones: a
        // scheduling failure must be diagnosable on its own, and never fails the booking.
        try
        {
            await _reminderScheduler.ScheduleForBookingAsync(session, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to schedule reminders for session {SessionId}.", session.Id);
        }

        return session.ToConfirmationDto(guestConfirmationQueued);
    }

    /// <summary>
    /// Rejects a submit that leaves one of the booking page's required custom
    /// fields blank.
    ///
    /// Deliberately here rather than inside BookingSession.Submit, even though
    /// that method already guards name/email/date/time. Those are genuinely
    /// always true of a submitted booking; "every required field is answered"
    /// is only true relative to the page's configuration *at this moment* - an
    /// organizer can add a required field tomorrow, and that must not
    /// retroactively make yesterday's bookings invalid. It is page policy, the
    /// same distinction AvailabilityException's 366-day cap draws between a
    /// validator rule and a domain invariant.
    ///
    /// A ValidationException (rather than a DomainException) so the response
    /// carries a per-field error map in the project's standard shape, letting
    /// the wizard point at the field that is missing.
    /// </summary>
    private async Task EnsureRequiredAnswersGivenAsync(BookingSession session, CancellationToken cancellationToken)
    {
        var requiredFields = await _db.BookingPages
            .AsNoTracking()
            .Where(p => p.Id == session.BookingPageId)
            .SelectMany(p => p.FormFields)
            .Where(f => f.IsRequired)
            .Select(f => new { f.Id, f.Label })
            .ToListAsync(cancellationToken);

        if (requiredFields.Count == 0) return;

        var answered = session.Answers
            .Where(a => !string.IsNullOrWhiteSpace(a.Value))
            .Select(a => a.BookingFormFieldId)
            .ToHashSet();

        var missing = requiredFields
            .Where(f => !answered.Contains(f.Id))
            .Select(f => new ValidationFailure(
                BookingFieldNames.ForCustomField(f.Id),
                $"{f.Label} is required."))
            .ToList();

        if (missing.Count > 0) throw new ValidationException(missing);
    }
}
