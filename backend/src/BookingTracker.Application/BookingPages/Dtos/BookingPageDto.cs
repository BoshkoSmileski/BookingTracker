namespace BookingTracker.Application.BookingPages.Dtos;

/// <param name="Instructions">
/// What the organizer wants visitors to read before booking. Public on purpose:
/// this is the only channel the feature has, and until it was added here the
/// instructions an organizer wrote were stored but never shown to anybody.
/// </param>
/// <param name="FormFields">
/// The questions the visitor is asked on the details step. Public for the same
/// reason Instructions is: the wizard cannot render an input for a field it was
/// never told about.
/// </param>
public record BookingPageDto(
    Guid Id,
    string Slug,
    string Title,
    string? Description,
    string OrganizerName,
    int DurationMinutes,
    int BufferBeforeMinutes,
    int BufferAfterMinutes,
    /// <summary>
    /// "None" or "GoogleMeet". Public so the wizard can tell a visitor this is
    /// an online meeting *before* they book, rather than only in the
    /// confirmation email - the join link itself does not exist until the
    /// booking is made, but whether there will be one is known up front.
    /// </summary>
    string MeetingProvider,
    /// <summary>
    /// The organizer's IANA time zone, from their <c>WorkingSchedule</c>.
    /// "UTC" when they have no schedule yet (in which case there are no slots
    /// to label either).
    ///
    /// Public because every time this API hands a visitor is organizer-local
    /// wall clock - <c>AvailableSlotDto.LocalStartTime</c>,
    /// <c>BookingSession.SelectedTime</c>, the confirmation email, the ICS
    /// invitation - and until this was here the wizard had no way to say whose
    /// clock those readings are on. It is a label for values already being
    /// sent, not a second source of time: no conversion is performed from it.
    /// </summary>
    string TimeZoneId,
    IReadOnlyList<BookingInstructionDto> Instructions,
    IReadOnlyList<BookingFormFieldDto> FormFields);
