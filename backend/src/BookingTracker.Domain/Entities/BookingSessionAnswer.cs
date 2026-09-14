using BookingTracker.Domain.Common;

namespace BookingTracker.Domain.Entities;

/// <summary>
/// A visitor's answer to one <see cref="BookingFormField"/>, held on the
/// BookingSession projection exactly like Name/Email/Phone/Message are.
///
/// Like every other value on that projection this is derived state, never a
/// side channel: it is only ever written by BookingSession.Apply in response to
/// a FieldChanged event, so BookingSession.Rebuild reconstructs the full answer
/// set from the event log alone. Nothing outside the aggregate may construct or
/// mutate one.
/// </summary>
public sealed class BookingSessionAnswer : Entity<Guid>
{
    public Guid BookingSessionId { get; private set; }

    /// <summary>The BookingFormField this answers. A plain id, not a navigation - the field lives on the BookingPage aggregate.</summary>
    public Guid BookingFormFieldId { get; private set; }

    public string Value { get; private set; } = default!;

    private BookingSessionAnswer() { }

    internal static BookingSessionAnswer Create(Guid bookingSessionId, Guid bookingFormFieldId, string value) => new()
    {
        Id = Guid.NewGuid(),
        BookingSessionId = bookingSessionId,
        BookingFormFieldId = bookingFormFieldId,
        Value = value
    };

    internal void UpdateValue(string value) => Value = value;
}
