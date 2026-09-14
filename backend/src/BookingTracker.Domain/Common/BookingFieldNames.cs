namespace BookingTracker.Domain.Common;

/// <summary>
/// Known values for BookingSessionEvent.FieldName. Kept as string constants
/// rather than an enum so FieldChanged stays open to new form fields without
/// a schema/enum change.
/// </summary>
public static class BookingFieldNames
{
    public const string Name = "Name";
    public const string Email = "Email";
    public const string Phone = "Phone";
    public const string Message = "Message";
    public const string Date = "Date";
    public const string Time = "Time";

    /// <summary>
    /// Marks a FieldName as an answer to an organizer-defined BookingFormField
    /// rather than one of the four built-in fields above. This is the whole
    /// mechanism behind custom booking questions: because FieldName is an open
    /// string discriminator, a custom answer is an ordinary FieldChanged event
    /// and needs no new BookingEventType and no change to Rebuild.
    ///
    /// "custom:" + a 36-character Guid is 43 characters, comfortably inside
    /// <see cref="BookingFieldLimits.EventFieldNameMaxLength"/> (50) - the
    /// column that actually stores it.
    /// </summary>
    public const string CustomFieldPrefix = "custom:";

    /// <summary>The FieldName that carries answers for <paramref name="fieldId"/>.</summary>
    public static string ForCustomField(Guid fieldId) => $"{CustomFieldPrefix}{fieldId:D}";

    /// <summary>
    /// The BookingFormField id encoded in <paramref name="fieldName"/>, or null
    /// if this is not a custom field name. The one place that decodes the
    /// convention <see cref="ForCustomField"/> encodes, so the two cannot drift.
    /// </summary>
    public static Guid? TryGetCustomFieldId(string? fieldName)
    {
        if (fieldName is null || !fieldName.StartsWith(CustomFieldPrefix, StringComparison.Ordinal)) return null;
        return Guid.TryParse(fieldName.AsSpan(CustomFieldPrefix.Length), out var id) ? id : null;
    }
}
