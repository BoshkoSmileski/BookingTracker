namespace BookingTracker.Domain.Common;

/// <summary>
/// Single source of truth for every character limit applied to a booking
/// session's user-supplied text - referenced by the EF Core configurations
/// that define the actual SQL Server columns (Infrastructure), the
/// FluentValidation validators that reject bad input before it reaches a
/// handler (Application), and BookingSession's own domain invariants
/// (this project). Change a limit here and the schema (via a new migration),
/// the validators, and the domain guards all move together instead of
/// silently drifting apart in three unrelated files.
/// </summary>
public static class BookingFieldLimits
{
    public const int NameMaxLength = 200;
    public const int EmailMaxLength = 320;
    public const int PhoneMaxLength = 50;

    /// <summary>
    /// BookingSessions.Message had no database limit at all (nvarchar(max))
    /// before this constant existed - there was no real column limit to
    /// "match". This is a deliberately chosen application-level cap generous
    /// enough for any legitimate pre-meeting note, now also applied to the
    /// column itself (see the AddBookingSessionMessageMaxLength migration)
    /// so it becomes one real, matched limit instead of a soft one that
    /// could quietly drift from the schema.
    /// </summary>
    public const int MessageMaxLength = 2000;

    public const int CancellationReasonMaxLength = 500;

    /// <summary>
    /// The label an organizer gives a custom booking form field ("Company",
    /// "What would you like to discuss?").
    /// </summary>
    public const int CustomFieldLabelMaxLength = 150;

    /// <summary>
    /// A visitor's answer to a custom booking form field. One limit for both
    /// ShortText and LongText on purpose: the field type drives the input
    /// control, not the storage, because an organizer can change a field's type
    /// after answers already exist and a type-dependent limit would then
    /// retroactively invalidate stored data. Matches the column via
    /// BookingSessionConfiguration, like every other limit here.
    /// </summary>
    public const int CustomAnswerMaxLength = 2000;

    /// <summary>
    /// The join URL of a booking's online meeting. Not user-editable - Google
    /// generates it - so unlike every other limit here it has no
    /// FluentValidation rule in front of it: there is no request body a caller
    /// could put an over-long value into. The column limit and the matching
    /// guard in BookingSession.AssignMeetingLink are therefore the two
    /// enforcement points rather than the usual three, and the guard exists
    /// precisely so a provider returning something absurd fails as a
    /// DomainException rather than as an SQL truncation.
    ///
    /// A Google Meet URL is ~50 characters; 500 leaves room for the far longer
    /// links other providers issue (Zoom and Teams both embed encoded
    /// meeting/passcode state in the query string).
    /// </summary>
    public const int MeetingUrlMaxLength = 500;

    public const int ClientIpMaxLength = 45;
    public const int UserAgentMaxLength = 500;
    public const int EventFieldNameMaxLength = 50;
}
