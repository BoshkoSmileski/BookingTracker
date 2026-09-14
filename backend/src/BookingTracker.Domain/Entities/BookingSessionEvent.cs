using BookingTracker.Domain.Common;
using BookingTracker.Domain.Enums;
using BookingTracker.Domain.ValueObjects;

namespace BookingTracker.Domain.Entities;

/// <summary>
/// A single immutable fact in a booking session's history. Rows in this table
/// are append-only: there is deliberately no setter and no update method.
/// The only way to create one is through the static factory methods below,
/// which mirror the domain events a booking session can produce.
/// </summary>
public sealed class BookingSessionEvent : Entity<long>
{
    public Guid SessionId { get; private set; }
    public Guid BookingPageId { get; private set; }
    public BookingEventType EventType { get; private set; }
    public string? FieldName { get; private set; }
    public string? OldValue { get; private set; }
    public string? NewValue { get; private set; }
    public int ClientSequenceNumber { get; private set; }
    public DateTime Timestamp { get; private set; }
    public ClientContext Context { get; private set; } = ClientContext.Unknown;

    private BookingSessionEvent() { }

    private static BookingSessionEvent Create(
        Guid sessionId,
        Guid bookingPageId,
        BookingEventType eventType,
        string? fieldName,
        string? oldValue,
        string? newValue,
        int clientSequenceNumber,
        ClientContext context)
    {
        return new BookingSessionEvent
        {
            SessionId = sessionId,
            BookingPageId = bookingPageId,
            EventType = eventType,
            FieldName = fieldName,
            OldValue = oldValue,
            NewValue = newValue,
            ClientSequenceNumber = clientSequenceNumber,
            Timestamp = DateTime.UtcNow,
            // Cloned rather than assigned directly: EF Core's owned-type change
            // tracker identifies owned instances by reference, so passing the
            // same ClientContext object into multiple events (or into both an
            // event and the session) makes it look like one owned instance is
            // shared across owners, which corrupts tracking after SaveChanges.
            Context = context with { }
        };
    }

    public static BookingSessionEvent SessionStarted(Guid sessionId, Guid bookingPageId, int clientSequenceNumber, ClientContext context)
        => Create(sessionId, bookingPageId, BookingEventType.SessionStarted, null, null, null, clientSequenceNumber, context);

    public static BookingSessionEvent FieldChanged(Guid sessionId, Guid bookingPageId, string fieldName, string? oldValue, string? newValue, int clientSequenceNumber, ClientContext context)
        => Create(sessionId, bookingPageId, BookingEventType.FieldChanged, fieldName, oldValue, newValue, clientSequenceNumber, context);

    public static BookingSessionEvent DateSelected(Guid sessionId, Guid bookingPageId, string? oldValue, string? newValue, int clientSequenceNumber, ClientContext context)
        => Create(sessionId, bookingPageId, BookingEventType.DateSelected, BookingFieldNames.Date, oldValue, newValue, clientSequenceNumber, context);

    public static BookingSessionEvent TimeSelected(Guid sessionId, Guid bookingPageId, string? oldValue, string? newValue, int clientSequenceNumber, ClientContext context)
        => Create(sessionId, bookingPageId, BookingEventType.TimeSelected, BookingFieldNames.Time, oldValue, newValue, clientSequenceNumber, context);

    public static BookingSessionEvent UserInactive(Guid sessionId, Guid bookingPageId, int clientSequenceNumber, ClientContext context)
        => Create(sessionId, bookingPageId, BookingEventType.UserInactive, null, null, null, clientSequenceNumber, context);

    public static BookingSessionEvent UserActive(Guid sessionId, Guid bookingPageId, int clientSequenceNumber, ClientContext context)
        => Create(sessionId, bookingPageId, BookingEventType.UserActive, null, null, null, clientSequenceNumber, context);

    public static BookingSessionEvent BrowserClosed(Guid sessionId, Guid bookingPageId, int clientSequenceNumber, ClientContext context)
        => Create(sessionId, bookingPageId, BookingEventType.BrowserClosed, null, null, null, clientSequenceNumber, context);

    /// <summary>
    /// The one event that carries generated data rather than a before/after pair:
    /// OldValue holds the new BookingReference, NewValue the new PublicToken, so
    /// BookingSession.Apply() can restore both when replaying the log.
    /// </summary>
    public static BookingSessionEvent BookingSubmitted(Guid sessionId, Guid bookingPageId, string bookingReference, string publicToken, int clientSequenceNumber, ClientContext context)
        => Create(sessionId, bookingPageId, BookingEventType.BookingSubmitted, null, bookingReference, publicToken, clientSequenceNumber, context);

    public static BookingSessionEvent BookingAbandoned(Guid sessionId, Guid bookingPageId, int clientSequenceNumber)
        => Create(sessionId, bookingPageId, BookingEventType.BookingAbandoned, null, null, null, clientSequenceNumber, ClientContext.Unknown);

    public static BookingSessionEvent BookingCancelled(Guid sessionId, Guid bookingPageId, string cancelledBy, string? reason, int clientSequenceNumber, ClientContext context)
        => Create(sessionId, bookingPageId, BookingEventType.BookingCancelled, cancelledBy, null, reason, clientSequenceNumber, context);

    public static BookingSessionEvent BookingRescheduled(Guid sessionId, Guid bookingPageId, string? oldValue, string? newValue, int clientSequenceNumber, ClientContext context)
        => Create(sessionId, bookingPageId, BookingEventType.BookingRescheduled, "DateTime", oldValue, newValue, clientSequenceNumber, context);

    public static BookingSessionEvent ReminderSent(Guid sessionId, Guid bookingPageId, string window, string recipientEmail, int clientSequenceNumber)
        => Create(sessionId, bookingPageId, BookingEventType.ReminderSent, window, null, recipientEmail, clientSequenceNumber, ClientContext.Unknown);

    public static BookingSessionEvent EmailSent(Guid sessionId, Guid bookingPageId, string templateName, string recipientEmail, int clientSequenceNumber)
        => Create(sessionId, bookingPageId, BookingEventType.EmailSent, templateName, null, recipientEmail, clientSequenceNumber, ClientContext.Unknown);

    /// <summary>
    /// FieldName carries the MeetingProviderType name and NewValue the join
    /// URL - the same "two generated values in one event" shape BookingSubmitted
    /// already uses, so BookingSession.Apply can restore both when replaying.
    /// System-triggered (calendar sync, not a visitor request), hence
    /// ClientContext.Unknown like the other server-side events here.
    /// </summary>
    public static BookingSessionEvent MeetingLinkAssigned(Guid sessionId, Guid bookingPageId, string provider, string meetingUrl, int clientSequenceNumber)
        => Create(sessionId, bookingPageId, BookingEventType.MeetingLinkAssigned, provider, null, meetingUrl, clientSequenceNumber, ClientContext.Unknown);
}
