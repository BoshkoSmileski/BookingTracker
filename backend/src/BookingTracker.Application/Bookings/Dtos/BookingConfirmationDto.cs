using BookingTracker.Application.BookingSessions.Dtos;

namespace BookingTracker.Application.Bookings.Dtos;

/// <summary>
/// Returned only by Submit/Cancel/Reschedule (the actions that own/change a
/// booking) - deliberately separate from BookingSessionDto, which is returned
/// by session-id-keyed endpoints a visitor can hit before OR after submitting.
/// PublicToken must never appear in that broader-surface DTO: it's the sole
/// credential for public self-service management, and leaking it through a
/// session-id lookup would let anyone who observes a session id (logs,
/// history, a shared link) take over a booking they didn't make.
/// </summary>
public record BookingConfirmationDto(
    Guid Id,
    Guid BookingPageId,
    string Status,
    string? Name,
    string? Email,
    string? Phone,
    string? Message,
    DateOnly? SelectedDate,
    TimeOnly? SelectedTime,
    DateTime CreatedAt,
    DateTime LastActivityAt,
    DateTime? SubmittedAt,
    DateTime? AbandonedAt,
    DateTime? CancelledAt,
    DateTime? RescheduledAt,
    string? BookingReference,
    string? PublicToken,
    /// <summary>"GoogleMeet", or null when this booking has no online meeting.</summary>
    string? MeetingProvider,
    /// <summary>
    /// The join URL, or null. Populated on the submit response because calendar
    /// sync now runs before this DTO is built (see
    /// SubmitBookingSessionCommandHandler) - which is what lets the wizard's
    /// success step offer "Join Google Meet" immediately rather than telling the
    /// guest to go and find the confirmation email.
    /// </summary>
    string? MeetingUrl,
    IReadOnlyList<BookingSessionAnswerDto> Answers,
    /// <summary>
    /// Whether a guest-facing confirmation of <i>this</i> action was queued -
    /// the booking confirmation on submit, the cancellation confirmation on
    /// cancel, the reschedule confirmation on reschedule. All three are called
    /// confirmations by the templates that render them.
    ///
    /// <b>Queued, never sent.</b> Sending happens later and out of the request
    /// (EmailQueueProcessor), so this is the strongest claim the response can
    /// honestly make, and it is what the guest screens key their "on its way"
    /// wording off. False is ordinary rather than exceptional: the organizer
    /// may have guest notifications switched off, or queueing may have failed -
    /// queueing is best-effort and never fails the booking. Either way the
    /// screen simply does not promise an email.
    /// </summary>
    bool GuestConfirmationQueued);
