using System.Net.Mail;
using BookingTracker.Domain.Common;
using BookingTracker.Domain.Enums;
using BookingTracker.Domain.Exceptions;
using BookingTracker.Domain.ValueObjects;

namespace BookingTracker.Domain.Entities;

/// <summary>
/// Aggregate root representing the CURRENT, materialized state of a visitor's
/// booking session - the read model. Every mutating method validates a rule,
/// builds the immutable BookingSessionEvent that describes what happened, and
/// funnels it through the single <see cref="Apply"/> method that actually
/// mutates state. <see cref="Rebuild"/> reuses that same Apply method to
/// reconstruct a session purely by replaying its persisted event log, which is
/// what proves the projection table is nothing more than a cache of the log.
/// </summary>
public sealed class BookingSession : Entity<Guid>
{
    private readonly List<BookingSessionAnswer> _answers = [];

    public Guid BookingPageId { get; private set; }
    public BookingSessionStatus Status { get; private set; }
    public string? Name { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? Message { get; private set; }
    public DateOnly? SelectedDate { get; private set; }
    public TimeOnly? SelectedTime { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime LastActivityAt { get; private set; }
    public DateTime? SubmittedAt { get; private set; }
    public DateTime? AbandonedAt { get; private set; }
    public int LastClientSequenceNumber { get; private set; }
    public ClientContext LastContext { get; private set; } = ClientContext.Unknown;

    /// <summary>Short human-friendly support reference, assigned once at Submit and never changed - not a secret.</summary>
    public string? BookingReference { get; private set; }

    /// <summary>The sole credential for public self-service management - never equal to Id.</summary>
    public string? PublicToken { get; private set; }

    public DateTime? CancelledAt { get; private set; }
    public CancelledByType? CancelledBy { get; private set; }
    public string? CancellationReason { get; private set; }

    public DateTime? RescheduledAt { get; private set; }
    public int RescheduleCount { get; private set; }

    /// <summary>
    /// Which kind of online meeting this booking has, snapshotted from the
    /// booking page when the meeting was created. Null means no meeting was
    /// ever created for it - either the page is In person, or the organizer
    /// has no connected calendar to create one on.
    ///
    /// Snapshotted rather than read from BookingPage.MeetingProvider on demand
    /// because the page's setting is what *future* bookings get: an organizer
    /// switching a page to In person must not blank the Meet link a guest is
    /// already holding in their inbox.
    /// </summary>
    public MeetingProviderType? MeetingProvider { get; private set; }

    /// <summary>
    /// The join URL for <see cref="MeetingProvider"/>. Stored, not derived:
    /// Google mints it and returns it exactly once, in the response to the
    /// event insert that created it, so there is nothing to recompute it from.
    /// One booking has one meeting for its whole life - see
    /// <see cref="AssignMeetingLink"/>.
    /// </summary>
    public string? MeetingUrl { get; private set; }

    /// <summary>
    /// Answers to the booking page's organizer-defined custom fields. Part of
    /// the projection exactly like Name/Email/Phone/Message: written only by
    /// <see cref="Apply"/> in response to a FieldChanged event, so
    /// <see cref="Rebuild"/> reconstructs them from the log like everything else.
    /// </summary>
    public IReadOnlyList<BookingSessionAnswer> Answers => _answers.AsReadOnly();

    private BookingSession() { }

    public static (BookingSession Session, BookingSessionEvent Event) Start(Guid bookingPageId, ClientContext context)
    {
        var session = new BookingSession { Id = Guid.NewGuid(), BookingPageId = bookingPageId };
        var @event = BookingSessionEvent.SessionStarted(session.Id, bookingPageId, 0, context);
        session.Apply(@event);
        return (session, @event);
    }

    /// <summary>
    /// Reconstructs a session's current state from nothing but its event log,
    /// in ClientSequenceNumber order. Used by the /rebuild diagnostic endpoint
    /// to prove the BookingSessions projection table never holds information
    /// that isn't already derivable from BookingSessionEvents.
    /// </summary>
    public static BookingSession Rebuild(Guid bookingPageId, IEnumerable<BookingSessionEvent> orderedEvents)
    {
        var session = new BookingSession { BookingPageId = bookingPageId };
        foreach (var @event in orderedEvents)
        {
            session.Id = @event.SessionId;
            session.Apply(@event);
        }
        return session;
    }

    public BookingSessionEvent ChangeField(string fieldName, string? newValue, int clientSequenceNumber, ClientContext context)
    {
        EnsureActive();

        // A custom field's answer travels the same path as the four built-in
        // fields, distinguished only by its FieldName - which is exactly what
        // BookingEventType/BookingFieldNames were left open-ended for. No new
        // event type, and Rebuild keeps working with no changes at all.
        var customFieldId = BookingFieldNames.TryGetCustomFieldId(fieldName);

        string? oldValue = customFieldId is { } id
            ? _answers.FirstOrDefault(a => a.BookingFormFieldId == id)?.Value
            : fieldName switch
            {
                BookingFieldNames.Name => Name,
                BookingFieldNames.Email => Email,
                BookingFieldNames.Phone => Phone,
                BookingFieldNames.Message => Message,
                _ => throw new DomainException($"Unknown booking field '{fieldName}'.")
            };

        // Length only, never format: this runs on every keystroke the visitor
        // sends (the frontend reports partial, in-progress values, not just
        // the final one - that's the whole point of the event-sourced
        // tracker), so a format check here would reject legitimate
        // mid-typing values like "jane@exam". A too-long value is never
        // "not finished yet" though - it's already invalid - so rejecting it
        // immediately is correct and matches AppendBookingEventsCommandValidator's
        // equivalent front-line check, both sourced from BookingFieldLimits.
        EnsureWithinFieldLimit(fieldName, newValue);

        var @event = BookingSessionEvent.FieldChanged(Id, BookingPageId, fieldName, oldValue, newValue, clientSequenceNumber, context);
        Apply(@event);
        return @event;
    }

    private static void EnsureWithinFieldLimit(string fieldName, string? value)
    {
        if (value is null) return;

        var limit = BookingFieldNames.TryGetCustomFieldId(fieldName) is not null
            ? BookingFieldLimits.CustomAnswerMaxLength
            : fieldName switch
            {
                BookingFieldNames.Name => BookingFieldLimits.NameMaxLength,
                BookingFieldNames.Email => BookingFieldLimits.EmailMaxLength,
                BookingFieldNames.Phone => BookingFieldLimits.PhoneMaxLength,
                BookingFieldNames.Message => BookingFieldLimits.MessageMaxLength,
                _ => int.MaxValue,
            };

        if (value.Length > limit)
            throw new DomainException($"{fieldName} cannot exceed {limit} characters.");
    }

    public BookingSessionEvent SelectDate(DateOnly? newValue, int clientSequenceNumber, ClientContext context)
    {
        EnsureActive();
        var oldValue = SelectedDate?.ToString("O");
        var @event = BookingSessionEvent.DateSelected(Id, BookingPageId, oldValue, newValue?.ToString("O"), clientSequenceNumber, context);
        Apply(@event);
        return @event;
    }

    public BookingSessionEvent SelectTime(TimeOnly? newValue, int clientSequenceNumber, ClientContext context)
    {
        EnsureActive();
        var oldValue = SelectedTime?.ToString("O");
        var @event = BookingSessionEvent.TimeSelected(Id, BookingPageId, oldValue, newValue?.ToString("O"), clientSequenceNumber, context);
        Apply(@event);
        return @event;
    }

    public BookingSessionEvent MarkInactive(int clientSequenceNumber, ClientContext context)
    {
        var @event = BookingSessionEvent.UserInactive(Id, BookingPageId, clientSequenceNumber, context);
        Apply(@event);
        return @event;
    }

    public BookingSessionEvent MarkActive(int clientSequenceNumber, ClientContext context)
    {
        var @event = BookingSessionEvent.UserActive(Id, BookingPageId, clientSequenceNumber, context);
        Apply(@event);
        return @event;
    }

    public BookingSessionEvent MarkBrowserClosed(int clientSequenceNumber, ClientContext context)
    {
        var @event = BookingSessionEvent.BrowserClosed(Id, BookingPageId, clientSequenceNumber, context);
        Apply(@event);
        return @event;
    }

    public BookingSessionEvent Submit(int clientSequenceNumber, ClientContext context)
    {
        EnsureActive();
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Email) || SelectedDate is null || SelectedTime is null)
            throw new DomainException("A booking session must have a name, email, date, and time before it can be submitted.");

        // Format (not just presence) is checked here rather than on every
        // FieldChanged event: Email holds whatever the visitor last typed,
        // which is legitimately incomplete while they're still typing it -
        // Submit is the one point where it must actually be a real address,
        // since it's about to be used for a confirmation email.
        if (!IsValidEmail(Email))
            throw new DomainException($"'{Email}' is not a valid email address.");

        var @event = BookingSessionEvent.BookingSubmitted(
            Id, BookingPageId,
            SecureTokenGenerator.GenerateBookingReference(), SecureTokenGenerator.GeneratePublicToken(),
            clientSequenceNumber, context);
        Apply(@event);
        return @event;
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            return new MailAddress(email).Address == email.Trim();
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// System-triggered transition fired by the abandonment sweep, not by a client
    /// request, so there is no client-assigned sequence number to use - the next
    /// one is minted from the session's own counter instead.
    /// </summary>
    public BookingSessionEvent? Abandon()
    {
        if (Status != BookingSessionStatus.Active) return null;

        var @event = BookingSessionEvent.BookingAbandoned(Id, BookingPageId, LastClientSequenceNumber + 1);
        Apply(@event);
        return @event;
    }

    /// <summary>A cancelled booking is never deleted - only its Status/CancelledAt/CancelledBy/CancellationReason change.</summary>
    public BookingSessionEvent Cancel(CancelledByType cancelledBy, string? reason, ClientContext context)
    {
        EnsureSubmitted();
        if (reason is { Length: > BookingFieldLimits.CancellationReasonMaxLength })
            throw new DomainException($"Cancellation reason cannot exceed {BookingFieldLimits.CancellationReasonMaxLength} characters.");

        var @event = BookingSessionEvent.BookingCancelled(Id, BookingPageId, cancelledBy.ToString(), reason, LastClientSequenceNumber + 1, context);
        Apply(@event);
        return @event;
    }

    /// <summary>
    /// Records that an email went out. Routed through Apply (like every other
    /// event) purely so LastClientSequenceNumber keeps incrementing correctly
    /// across multiple logged emails in the same request - not because the
    /// event changes any other observable state.
    /// </summary>
    public BookingSessionEvent LogEmailSent(string templateName, string recipientEmail)
    {
        var @event = BookingSessionEvent.EmailSent(Id, BookingPageId, templateName, recipientEmail, LastClientSequenceNumber + 1);
        Apply(@event);
        return @event;
    }

    /// <summary>
    /// Records the online meeting created for this booking. Called only by
    /// calendar sync, which is the sole place the URL is ever known.
    ///
    /// Idempotent on purpose - one booking is one meeting. Re-assigning the
    /// URL the session already holds returns null and logs nothing, so a
    /// repeated sync (a reschedule, a retried create, a sweeper pass) can
    /// never append a second identical event or make a client think the link
    /// moved. A genuinely *different* URL is still recorded, because that is a
    /// real change the log has to describe - it happens when a booking that
    /// previously had no calendar event gets one for the first time.
    /// </summary>
    public BookingSessionEvent? AssignMeetingLink(MeetingProviderType provider, string meetingUrl)
    {
        if (provider == MeetingProviderType.None)
            throw new DomainException("A meeting link cannot be assigned for the None meeting provider.");
        if (string.IsNullOrWhiteSpace(meetingUrl))
            throw new DomainException("A meeting link cannot be empty.");
        if (meetingUrl.Length > BookingFieldLimits.MeetingUrlMaxLength)
            throw new DomainException($"Meeting URL cannot exceed {BookingFieldLimits.MeetingUrlMaxLength} characters.");

        if (MeetingProvider == provider && MeetingUrl == meetingUrl) return null;

        var @event = BookingSessionEvent.MeetingLinkAssigned(
            Id, BookingPageId, provider.ToString(), meetingUrl, LastClientSequenceNumber + 1);
        Apply(@event);
        return @event;
    }

    public BookingSessionEvent LogReminderSent(string window, string recipientEmail)
    {
        var @event = BookingSessionEvent.ReminderSent(Id, BookingPageId, window, recipientEmail, LastClientSequenceNumber + 1);
        Apply(@event);
        return @event;
    }

    /// <summary>
    /// Moves the booking to a new slot. There is no separate "release the old
    /// slot" step: because availability is always derived dynamically from the
    /// current SelectedDate/SelectedTime (never from a reserved row), the old
    /// slot becomes bookable again the instant this overwrites it.
    /// </summary>
    public BookingSessionEvent Reschedule(DateOnly newDate, TimeOnly newTime, ClientContext context)
    {
        EnsureSubmitted();
        var oldValue = $"{SelectedDate:O}|{SelectedTime:O}";
        var newValue = $"{newDate:O}|{newTime:O}";
        var @event = BookingSessionEvent.BookingRescheduled(Id, BookingPageId, oldValue, newValue, LastClientSequenceNumber + 1, context);
        Apply(@event);
        return @event;
    }

    /// <summary>
    /// The single place that turns an event into a state mutation. Called both
    /// right after a new event is created (the live path) and while replaying
    /// a persisted event log (the Rebuild path) - guaranteeing the two never
    /// drift apart.
    /// </summary>
    private void Apply(BookingSessionEvent @event)
    {
        switch (@event.EventType)
        {
            case BookingEventType.SessionStarted:
                CreatedAt = @event.Timestamp;
                Status = BookingSessionStatus.Active;
                break;

            case BookingEventType.FieldChanged:
                if (BookingFieldNames.TryGetCustomFieldId(@event.FieldName) is { } fieldId)
                {
                    ApplyAnswer(fieldId, @event.NewValue);
                    break;
                }

                switch (@event.FieldName)
                {
                    case BookingFieldNames.Name: Name = @event.NewValue; break;
                    case BookingFieldNames.Email: Email = @event.NewValue; break;
                    case BookingFieldNames.Phone: Phone = @event.NewValue; break;
                    case BookingFieldNames.Message: Message = @event.NewValue; break;
                }
                break;

            case BookingEventType.DateSelected:
                SelectedDate = string.IsNullOrEmpty(@event.NewValue) ? null : DateOnly.Parse(@event.NewValue);
                break;

            case BookingEventType.TimeSelected:
                SelectedTime = string.IsNullOrEmpty(@event.NewValue) ? null : TimeOnly.Parse(@event.NewValue);
                break;

            case BookingEventType.UserInactive:
            case BookingEventType.UserActive:
            case BookingEventType.BrowserClosed:
            case BookingEventType.ReminderSent:
            case BookingEventType.EmailSent:
                break;

            case BookingEventType.BookingSubmitted:
                Status = BookingSessionStatus.Submitted;
                SubmittedAt = @event.Timestamp;
                BookingReference = @event.OldValue;
                PublicToken = @event.NewValue;
                break;

            case BookingEventType.BookingAbandoned:
                Status = BookingSessionStatus.Abandoned;
                AbandonedAt = @event.Timestamp;
                break;

            case BookingEventType.BookingCancelled:
                Status = BookingSessionStatus.Cancelled;
                CancelledAt = @event.Timestamp;
                CancelledBy = Enum.Parse<CancelledByType>(@event.FieldName!);
                CancellationReason = @event.NewValue;
                break;

            case BookingEventType.MeetingLinkAssigned:
                MeetingProvider = Enum.Parse<MeetingProviderType>(@event.FieldName!);
                MeetingUrl = @event.NewValue;
                break;

            case BookingEventType.BookingRescheduled:
                var parts = @event.NewValue!.Split('|');
                SelectedDate = DateOnly.Parse(parts[0]);
                SelectedTime = TimeOnly.Parse(parts[1]);
                RescheduledAt = @event.Timestamp;
                RescheduleCount++;
                break;
        }

        LastActivityAt = @event.Timestamp;
        if (@event.ClientSequenceNumber > LastClientSequenceNumber) LastClientSequenceNumber = @event.ClientSequenceNumber;
        // Cloned for the same reason as in BookingSessionEvent.Create: an owned
        // ClientContext instance cannot be shared between two owners (this
        // session and the event) in EF Core's change tracker.
        LastContext = @event.Context with { };
    }

    /// <summary>
    /// Upserts one custom-field answer. A cleared value removes the row rather
    /// than storing an empty string, so "answered, then erased" and "never
    /// answered" are the same state - which is what a replay of the log has to
    /// produce for Rebuild to match the live projection, since a visitor who
    /// backspaces a field to empty must end up indistinguishable from one who
    /// never touched it.
    /// </summary>
    private void ApplyAnswer(Guid fieldId, string? value)
    {
        var existing = _answers.FirstOrDefault(a => a.BookingFormFieldId == fieldId);

        if (string.IsNullOrWhiteSpace(value))
        {
            if (existing is not null) _answers.Remove(existing);
            return;
        }

        if (existing is null) _answers.Add(BookingSessionAnswer.Create(Id, fieldId, value));
        else existing.UpdateValue(value);
    }

    private void EnsureActive()
    {
        if (Status != BookingSessionStatus.Active)
            throw new DomainException($"Booking session {Id} is {Status} and can no longer be modified.");
    }

    private void EnsureSubmitted()
    {
        if (Status != BookingSessionStatus.Submitted)
            throw new DomainException($"Booking session {Id} is {Status} and cannot be cancelled or rescheduled.");
    }
}
