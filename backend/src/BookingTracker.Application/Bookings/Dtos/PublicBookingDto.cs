namespace BookingTracker.Application.Bookings.Dtos;

/// <summary>What a visitor sees on the public "manage my booking" page - deliberately minimal.</summary>
public record PublicBookingDto(
    string BookingReference,
    string BookingPageSlug,
    string OrganizerName,
    /// <summary>
    /// The organizer's own address, so a guest holding nothing but a manage
    /// link has a way to reach the person they booked with - specifically once
    /// the booking is past or cancelled, where the screen can only tell them to
    /// "contact the organizer directly" and had, until now, no way to say how.
    ///
    /// This is the address the organizer already publishes by sending every
    /// confirmation, cancellation and reminder email from a booking page they
    /// chose to make public. Nothing else about the account is exposed here.
    /// </summary>
    string OrganizerEmail,
    string ServiceTitle,
    int DurationMinutes,
    DateOnly? SelectedDate,
    TimeOnly? SelectedTime,
    /// <summary>
    /// The organizer's IANA time zone - the clock <c>SelectedDate</c> and
    /// <c>SelectedTime</c> above are on. Sent so the guest's own page can say
    /// whose 10:00 this is, exactly as the booking wizard does; no conversion
    /// is done from it here or on the client.
    /// </summary>
    string TimeZoneId,
    /// <summary>
    /// The booking's real instant, and the end of it - the same pair
    /// <c>AvailableSlotDto</c> carries, resolved here against the organizer's
    /// own <c>TimeZoneInfo</c> exactly as slot generation resolved it when the
    /// slot was offered.
    ///
    /// It exists so no guest-side action has to rebuild the instant from
    /// <c>SelectedDate</c>/<c>SelectedTime</c>: those are organizer-local wall
    /// clock, and reconstructing a UTC moment from them in the browser reads
    /// them in the *visitor's* zone - the exact bug the wizard's downloadable
    /// .ics used to have. The wall-clock pair above stays what a guest is
    /// shown; this pair is what a calendar file is built from.
    ///
    /// Null only when the booking has no date or time yet, which a submitted
    /// booking never does.
    /// </summary>
    DateTime? StartUtc,
    DateTime? EndUtc,
    string Status,
    string? Name,
    string? Email,
    bool CanCancel,
    bool CanReschedule,
    /// <summary>"GoogleMeet", or null when this booking has no online meeting.</summary>
    string? MeetingProvider,
    /// <summary>
    /// The join URL, or null. Only supplied while the booking is still live -
    /// a cancelled or past booking keeps the value on the session as history,
    /// but showing a guest a join button for a meeting that is over (or whose
    /// Google event has been deleted) would be worse than showing nothing.
    /// </summary>
    string? MeetingUrl,
    /// <summary>
    /// The lead times, in minutes before the meeting, of the reminders that are
    /// <b>actually still scheduled</b> for this booking - read from the
    /// <c>BookingReminders</c> rows themselves, never re-derived from the
    /// organizer's NotificationSettings.
    ///
    /// That distinction is the whole point, and it is the reminder-scheduling rule
    /// read from the guest's side. Settings describe what a <i>future</i> booking
    /// would get; only a row proves this booking has one. So the list is empty -
    /// and the guest is told nothing - when reminders are switched off, when
    /// they were cancelled with the booking, and when they have already been
    /// sent (a sent reminder leaves <c>Scheduled</c>, and "we'll remind you"
    /// would then be false rather than merely unhelpful).
    ///
    /// Ascending, so a screen can read them out in the order they will arrive.
    /// </summary>
    IReadOnlyList<int> ReminderLeadMinutes);
