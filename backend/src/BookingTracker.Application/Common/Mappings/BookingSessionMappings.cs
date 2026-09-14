using BookingTracker.Application.BookingSessions.Dtos;
using BookingTracker.Application.Bookings.Dtos;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;

namespace BookingTracker.Application.Common.Mappings;

public static class BookingSessionMappings
{
    public static BookingSessionDto ToDto(this BookingSession session) => new(
        session.Id,
        session.BookingPageId,
        session.Status.ToString(),
        session.Name,
        session.Email,
        session.Phone,
        session.Message,
        session.SelectedDate,
        session.SelectedTime,
        session.CreatedAt,
        session.LastActivityAt,
        session.SubmittedAt,
        session.AbandonedAt,
        session.MeetingProvider?.ToString(),
        session.MeetingUrl,
        session.ToAnswerDtos());

    /// <summary>
    /// Answers ordered by field id rather than by the page's DisplayOrder: this
    /// mapping is a pure function of the session and never loads the page, so
    /// display order is applied by the two screens that render them (both of
    /// which already hold the field list). A stable order here is still worth
    /// having - it keeps SignalR payloads byte-identical between broadcasts of
    /// an unchanged session.
    /// </summary>
    public static IReadOnlyList<BookingSessionAnswerDto> ToAnswerDtos(this BookingSession session) =>
        session.Answers
            .OrderBy(a => a.BookingFormFieldId)
            .Select(a => new BookingSessionAnswerDto(a.BookingFormFieldId, a.Value))
            .ToList();

    /// <summary>
    /// Includes PublicToken - only ever call this for the actor who legitimately
    /// owns the booking (see BookingConfirmationDto).
    /// </summary>
    /// <param name="guestConfirmationQueued">
    /// Whatever <see cref="Interfaces.IEmailNotificationService"/> reported for
    /// this action. Required rather than defaulted: a silently-false value would
    /// make a screen stop offering the "on its way" line for an email that was
    /// queued perfectly well, and a silently-true one would promise an email
    /// nobody wrote - the same reasoning that made BookingPageMappings.ToDto
    /// require its timeZoneId.
    /// </param>
    public static BookingConfirmationDto ToConfirmationDto(this BookingSession session, bool guestConfirmationQueued) => new(
        session.Id,
        session.BookingPageId,
        session.Status.ToString(),
        session.Name,
        session.Email,
        session.Phone,
        session.Message,
        session.SelectedDate,
        session.SelectedTime,
        session.CreatedAt,
        session.LastActivityAt,
        session.SubmittedAt,
        session.AbandonedAt,
        session.CancelledAt,
        session.RescheduledAt,
        session.BookingReference,
        session.PublicToken,
        session.MeetingProvider?.ToString(),
        session.MeetingUrl,
        session.ToAnswerDtos(),
        guestConfirmationQueued);

    /// <summary>
    /// The one place a stored event row becomes something the API hands out -
    /// every event-returning surface goes through here (both timeline routes,
    /// the email history, and the SignalR broadcast), which is why the
    /// redaction below belongs here and not at a call site.
    /// </summary>
    public static BookingSessionEventDto ToDto(this BookingSessionEvent @event) => new(
        @event.Id,
        @event.SessionId,
        @event.BookingPageId,
        @event.EventType.ToString(),
        @event.FieldName,
        @event.OldValue,
        RedactedNewValue(@event),
        @event.ClientSequenceNumber,
        @event.Timestamp,
        @event.Context.IpAddress,
        @event.Context.UserAgent);

    /// <summary>
    /// BookingSubmitted.NewValue is the booking's PublicToken - a bearer
    /// credential: holding it is sufficient to view, cancel and reschedule the
    /// booking with no other authentication (GetBookingByTokenQueryHandler
    /// resolves a session by nothing else). It is deliberately still WRITTEN to
    /// the event log in plaintext, because BookingSession.Apply restores it from
    /// there when replaying - see BookingSessionEvent.BookingSubmitted.
    /// Storage is not the problem; handing it out is.
    ///
    /// The route that leaked it was an anonymous GET
    /// /api/booking-sessions/{id}/timeline, whose handler ran an ownership check
    /// only when a RequestingOrganizerId was supplied - which that route never
    /// did. Anyone holding a session id (an identifier, kept in sessionStorage
    /// and sent in the path of every append-events call - never a secret) could
    /// read the token straight out of it and take the booking over. Reproduced
    /// in PublicTokenExposureTests, which is now the regression guard.
    ///
    /// That route was then removed outright (it had no caller), and
    /// RequestingOrganizerId required, so raw event rows are now reachable only
    /// through the organizer's own ownership-checked timeline. This redaction
    /// still stands and is still the thing being relied on there: the owning
    /// organizer does not receive the token either.
    ///
    /// Redacted for the owning organizer too, not just anonymous callers: they
    /// can already cancel and reschedule their own bookings through their own
    /// authenticated endpoints, so the token grants them nothing - while a
    /// credential rendered on a dashboard is one screenshot away from leaving
    /// the system. Least privilege, not a privilege boundary.
    ///
    /// OldValue (the booking reference) is left alone: it is a short support
    /// code that is printed in every confirmation email and is not accepted as a
    /// credential anywhere.
    ///
    /// Nothing consumes the redacted value. The timeline renders
    /// "Booking submitted" from the event type alone, and lib/eventDisplay.ts's
    /// hasValueDiff excludes BookingSubmitted, so no before/after pair is drawn
    /// for it. Rebuild is unaffected in principle as well as in practice -
    /// RebuildBookingSessionStateQueryHandler replays the ENTITIES straight from
    /// the DbContext and never passes through this mapping.
    /// </summary>
    private static string? RedactedNewValue(BookingSessionEvent @event) =>
        @event.EventType == BookingEventType.BookingSubmitted ? null : @event.NewValue;
}
