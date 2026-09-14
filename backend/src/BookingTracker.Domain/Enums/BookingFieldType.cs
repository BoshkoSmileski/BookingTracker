namespace BookingTracker.Domain.Enums;

/// <summary>
/// How a custom booking form field is presented to the visitor.
///
/// This governs the input control only, never the stored length: every answer
/// shares one column and one limit (BookingFieldLimits.CustomAnswerMaxLength),
/// because a second, type-dependent limit would be a page-configuration policy
/// that the session aggregate cannot check - an organizer can change a field
/// from LongText to ShortText after answers already exist.
/// </summary>
public enum BookingFieldType
{
    /// <summary>Single-line input.</summary>
    ShortText = 0,

    /// <summary>Multi-line textarea.</summary>
    LongText = 1
}
