namespace BookingTracker.Domain.Enums;

/// <summary>
/// Every possible interaction that can occur during a booking session.
/// Field-level edits (name/email/phone/message) are unified under FieldChanged
/// with FieldName as the discriminator, rather than one enum value per field,
/// so adding a new form field never requires an enum change.
/// </summary>
public enum BookingEventType
{
    SessionStarted = 0,
    FieldChanged = 1,
    DateSelected = 2,
    TimeSelected = 3,
    UserInactive = 4,
    UserActive = 5,
    BrowserClosed = 6,
    BookingSubmitted = 7,
    BookingAbandoned = 8,
    BookingCancelled = 9,
    BookingRescheduled = 10,
    ReminderSent = 11,

    /// <summary>
    /// Logged for every outbound email instead of a separate notification-log
    /// table - FieldName carries the template name (e.g. "BookingConfirmation")
    /// and NewValue the recipient, so a session's email history is just a
    /// filter over its existing event log.
    /// </summary>
    EmailSent = 12,

    /// <summary>
    /// An online meeting was created for this booking - FieldName carries the
    /// MeetingProviderType ("GoogleMeet") and NewValue the join URL.
    ///
    /// Unlike a custom form field's answer (which rides FieldChanged, because
    /// it is a value the *visitor* supplied), this is state the booking gains
    /// from an external system after submission, so it has no field to be a
    /// change of. It is an event rather than a bare column write for the
    /// reason every other fact about a session is: Rebuild() must be able to
    /// reconstruct it from the log alone.
    /// </summary>
    MeetingLinkAssigned = 13
}
