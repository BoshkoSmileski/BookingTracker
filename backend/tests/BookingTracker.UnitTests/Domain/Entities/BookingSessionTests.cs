using BookingTracker.Domain.Common;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.Domain.Exceptions;
using BookingTracker.UnitTests.TestSupport;

namespace BookingTracker.UnitTests.Domain.Entities;

/// <summary>
/// Exercises BookingSession's mutators and invariants directly (not through
/// Rebuild - see BookingSessionEventSourcingTests for that). Every mutator is
/// checked both for the state it produces and the event it returns, since the
/// event IS the thing that gets persisted and later replayed.
/// </summary>
public class BookingSessionTests
{
    private static readonly Guid PageId = Guid.NewGuid();

    [Fact]
    public void Start_CreatesActiveSession_WithSessionStartedEvent()
    {
        // Act
        var (session, @event) = BookingSession.Start(PageId, BookingSessionScenarios.SampleContext);

        // Assert
        Assert.Equal(BookingSessionStatus.Active, session.Status);
        Assert.Equal(PageId, session.BookingPageId);
        Assert.NotEqual(Guid.Empty, session.Id);
        Assert.Equal(BookingEventType.SessionStarted, @event.EventType);
        Assert.Equal(session.Id, @event.SessionId);
        Assert.Equal(0, @event.ClientSequenceNumber);
        Assert.Equal(session.CreatedAt, session.LastActivityAt);
    }

    /// <summary>
    /// The session id is the sole bearer capability for the anonymous
    /// resume flow (see SessionIdCapabilityTests for exactly what it can do), so
    /// how it is produced is a security property rather than an implementation
    /// detail - and the production creation path is the only place that matters.
    ///
    /// Asserted STRUCTURALLY, never statistically. A test that generated many
    /// ids and inspected their distribution would be a probabilistic claim
    /// dressed as a unit test, and would pass just as happily for a weak
    /// generator. What is checked instead is a fact that is true of every
    /// Guid.NewGuid() and false of every alternative the code could drift to -
    /// a sequential id, a counter, a caller-supplied value, or
    /// Guid.CreateVersion7 (time-ordered, and therefore partly predictable):
    /// RFC 4122 version 4 with the RFC variant bits.
    ///
    /// This does not prove the platform's RNG is strong. It proves the code
    /// asked for the random variant, which is the only half a test can own.
    /// </summary>
    [Fact]
    public void Start_AssignsARandomVersion4Identifier()
    {
        var (session, _) = BookingSession.Start(PageId, BookingSessionScenarios.SampleContext);

        var bytes = session.Id.ToByteArray();

        // Byte 7's high nibble is the version; byte 8's high bits are the variant.
        Assert.Equal(0x40, bytes[7] & 0xF0);
        Assert.Equal(0x80, bytes[8] & 0xC0);

        // And it is not derived from anything the caller supplied.
        Assert.NotEqual(PageId, session.Id);
        Assert.NotEqual(Guid.Empty, session.Id);
    }

    /// <summary>
    /// The companion fact: two sessions on the same booking page are never the
    /// same session. Distinctness, not randomness - a counter would satisfy this
    /// and is caught by the version assertion above, which is why both exist.
    /// </summary>
    [Fact]
    public void Start_AssignsADistinctIdentifierPerSession()
    {
        var (first, _) = BookingSession.Start(PageId, BookingSessionScenarios.SampleContext);
        var (second, _) = BookingSession.Start(PageId, BookingSessionScenarios.SampleContext);

        Assert.NotEqual(first.Id, second.Id);
    }

    [Theory]
    [InlineData(BookingFieldNames.Name)]
    [InlineData(BookingFieldNames.Email)]
    [InlineData(BookingFieldNames.Phone)]
    [InlineData(BookingFieldNames.Message)]
    public void ChangeField_UpdatesTheCorrespondingProperty(string fieldName)
    {
        // Arrange
        var (session, _) = BookingSession.Start(PageId, BookingSessionScenarios.SampleContext);

        // Act
        var @event = session.ChangeField(fieldName, "some value", 1, BookingSessionScenarios.SampleContext);

        // Assert
        Assert.Equal("some value", GetField(session, fieldName));
        Assert.Null(@event.OldValue);
        Assert.Equal("some value", @event.NewValue);
        Assert.Equal(fieldName, @event.FieldName);
    }

    [Fact]
    public void ChangeField_ReportsThePreviousValueAsOldValue()
    {
        // Arrange
        var (session, _) = BookingSession.Start(PageId, BookingSessionScenarios.SampleContext);
        session.ChangeField(BookingFieldNames.Name, "Jan", 1, BookingSessionScenarios.SampleContext);

        // Act
        var @event = session.ChangeField(BookingFieldNames.Name, "Jane", 2, BookingSessionScenarios.SampleContext);

        // Assert
        Assert.Equal("Jan", @event.OldValue);
        Assert.Equal("Jane", @event.NewValue);
        Assert.Equal("Jane", session.Name);
    }

    [Fact]
    public void ChangeField_UnknownFieldName_Throws()
    {
        var (session, _) = BookingSession.Start(PageId, BookingSessionScenarios.SampleContext);

        Assert.Throws<DomainException>(() => session.ChangeField("NotARealField", "x", 1, BookingSessionScenarios.SampleContext));
    }

    [Theory]
    [InlineData(BookingFieldNames.Name, 201)]
    [InlineData(BookingFieldNames.Email, 321)]
    [InlineData(BookingFieldNames.Phone, 51)]
    [InlineData(BookingFieldNames.Message, 2001)]
    public void ChangeField_ValueExceedingFieldLimit_Throws(string fieldName, int length)
    {
        var (session, _) = BookingSession.Start(PageId, BookingSessionScenarios.SampleContext);
        var tooLong = new string('a', length);

        Assert.Throws<DomainException>(() => session.ChangeField(fieldName, tooLong, 1, BookingSessionScenarios.SampleContext));
    }

    [Fact]
    public void ChangeField_ValueAtExactLimit_Succeeds()
    {
        var (session, _) = BookingSession.Start(PageId, BookingSessionScenarios.SampleContext);
        var exactly200 = new string('a', BookingFieldLimits.NameMaxLength);

        session.ChangeField(BookingFieldNames.Name, exactly200, 1, BookingSessionScenarios.SampleContext);

        Assert.Equal(exactly200, session.Name);
    }

    [Fact]
    public void ChangeField_AfterSessionIsNoLongerActive_Throws()
    {
        // Arrange
        var (session, _) = BookingSession.Start(PageId, BookingSessionScenarios.SampleContext);
        session.Abandon();

        // Act & Assert
        Assert.Throws<DomainException>(() => session.ChangeField(BookingFieldNames.Name, "x", 1, BookingSessionScenarios.SampleContext));
    }

    [Fact]
    public void SelectDate_And_SelectTime_UpdateTheSession()
    {
        var (session, _) = BookingSession.Start(PageId, BookingSessionScenarios.SampleContext);
        var date = new DateOnly(2026, 8, 10);
        var time = new TimeOnly(9, 30);

        session.SelectDate(date, 1, BookingSessionScenarios.SampleContext);
        session.SelectTime(time, 2, BookingSessionScenarios.SampleContext);

        Assert.Equal(date, session.SelectedDate);
        Assert.Equal(time, session.SelectedTime);
    }

    [Fact]
    public void Submit_MissingRequiredFields_Throws()
    {
        var (session, _) = BookingSession.Start(PageId, BookingSessionScenarios.SampleContext);

        var ex = Assert.Throws<DomainException>(() => session.Submit(1, BookingSessionScenarios.SampleContext));
        Assert.Contains("name, email, date, and time", ex.Message);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("missing-at-sign.com")]
    [InlineData("two@@signs.com")]
    public void Submit_InvalidEmailFormat_Throws(string invalidEmail)
    {
        var (session, _) = BookingSession.Start(PageId, BookingSessionScenarios.SampleContext);
        session.ChangeField(BookingFieldNames.Name, "Jane", 1, BookingSessionScenarios.SampleContext);
        session.ChangeField(BookingFieldNames.Email, invalidEmail, 2, BookingSessionScenarios.SampleContext);
        session.SelectDate(new DateOnly(2026, 8, 10), 3, BookingSessionScenarios.SampleContext);
        session.SelectTime(new TimeOnly(9, 0), 4, BookingSessionScenarios.SampleContext);

        Assert.Throws<DomainException>(() => session.Submit(5, BookingSessionScenarios.SampleContext));
    }

    [Fact]
    public void Submit_ValidSession_TransitionsToSubmitted_AndAssignsReferenceAndToken()
    {
        // Act
        var result = BookingSessionScenarios.StartFillAndSubmit(PageId);

        // Assert
        var session = result.Session;
        Assert.Equal(BookingSessionStatus.Submitted, session.Status);
        Assert.NotNull(session.SubmittedAt);
        Assert.False(string.IsNullOrWhiteSpace(session.BookingReference));
        Assert.False(string.IsNullOrWhiteSpace(session.PublicToken));
        Assert.NotEqual(session.BookingReference, session.PublicToken);
        // PublicToken must never equal the entity Id - it's a separate, unguessable credential.
        Assert.NotEqual(session.Id.ToString(), session.PublicToken);
    }

    [Fact]
    public void Submit_AfterAlreadySubmitted_Throws()
    {
        var result = BookingSessionScenarios.StartFillAndSubmit(PageId);

        Assert.Throws<DomainException>(() => result.Session.Submit(99, BookingSessionScenarios.SampleContext));
    }

    [Fact]
    public void Abandon_WhileActive_TransitionsToAbandoned()
    {
        var (session, _) = BookingSession.Start(PageId, BookingSessionScenarios.SampleContext);

        var @event = session.Abandon();

        Assert.NotNull(@event);
        Assert.Equal(BookingSessionStatus.Abandoned, session.Status);
        Assert.NotNull(session.AbandonedAt);
    }

    [Fact]
    public void Abandon_WhenNotActive_ReturnsNullAndDoesNotChangeStatus()
    {
        var result = BookingSessionScenarios.StartFillAndSubmit(PageId);

        var @event = result.Session.Abandon();

        Assert.Null(@event);
        Assert.Equal(BookingSessionStatus.Submitted, result.Session.Status);
    }

    [Fact]
    public void Cancel_BeforeSubmission_Throws()
    {
        var (session, _) = BookingSession.Start(PageId, BookingSessionScenarios.SampleContext);

        Assert.Throws<DomainException>(() => session.Cancel(CancelledByType.Customer, "reason", BookingSessionScenarios.SampleContext));
    }

    [Fact]
    public void Cancel_AfterSubmission_TransitionsToCancelled_AndRecordsReasonAndActor()
    {
        var result = BookingSessionScenarios.StartFillAndSubmit(PageId);

        result.Session.Cancel(CancelledByType.Organizer, "Double-booked", BookingSessionScenarios.SampleContext);

        Assert.Equal(BookingSessionStatus.Cancelled, result.Session.Status);
        Assert.Equal(CancelledByType.Organizer, result.Session.CancelledBy);
        Assert.Equal("Double-booked", result.Session.CancellationReason);
        Assert.NotNull(result.Session.CancelledAt);
    }

    [Fact]
    public void Cancel_ReasonExceedingLimit_Throws()
    {
        var result = BookingSessionScenarios.StartFillAndSubmit(PageId);
        var tooLong = new string('x', BookingFieldLimits.CancellationReasonMaxLength + 1);

        Assert.Throws<DomainException>(() => result.Session.Cancel(CancelledByType.Customer, tooLong, BookingSessionScenarios.SampleContext));
    }

    [Fact]
    public void Reschedule_BeforeSubmission_Throws()
    {
        var (session, _) = BookingSession.Start(PageId, BookingSessionScenarios.SampleContext);

        Assert.Throws<DomainException>(() =>
            session.Reschedule(new DateOnly(2026, 9, 1), new TimeOnly(10, 0), BookingSessionScenarios.SampleContext));
    }

    [Fact]
    public void Reschedule_UpdatesDateAndTime_AndIncrementsCount()
    {
        var result = BookingSessionScenarios.StartFillAndSubmit(PageId);
        var newDate = new DateOnly(2026, 9, 1);
        var newTime = new TimeOnly(14, 0);

        result.Session.Reschedule(newDate, newTime, BookingSessionScenarios.SampleContext);

        Assert.Equal(newDate, result.Session.SelectedDate);
        Assert.Equal(newTime, result.Session.SelectedTime);
        Assert.Equal(1, result.Session.RescheduleCount);
        Assert.NotNull(result.Session.RescheduledAt);
    }

    [Fact]
    public void Reschedule_MultipleTimes_IncrementsCountEachTime()
    {
        var result = BookingSessionScenarios.SubmitThenReschedule(
            PageId,
            (2026, 9, 1, 10, 0),
            (2026, 9, 2, 11, 0),
            (2026, 9, 3, 12, 0));

        Assert.Equal(3, result.Session.RescheduleCount);
        Assert.Equal(new DateOnly(2026, 9, 3), result.Session.SelectedDate);
        Assert.Equal(new TimeOnly(12, 0), result.Session.SelectedTime);
    }

    [Fact]
    public void LogEmailSent_IncrementsSequenceNumber_WithoutChangingOtherState()
    {
        var result = BookingSessionScenarios.StartFillAndSubmit(PageId);
        var seqBefore = result.Session.LastClientSequenceNumber;
        var statusBefore = result.Session.Status;

        result.Session.LogEmailSent("BookingConfirmation", "jane@example.com");

        Assert.Equal(seqBefore + 1, result.Session.LastClientSequenceNumber);
        Assert.Equal(statusBefore, result.Session.Status);
    }

    [Fact]
    public void ClientContext_IsClonedNotSharedBetweenSessionAndEvent()
    {
        // EF's owned-type tracker identifies instances by reference - LastContext
        // must be its own clone, never literally the same instance as the event's.
        var (session, @event) = BookingSession.Start(PageId, BookingSessionScenarios.SampleContext);

        Assert.NotSame(@event.Context, session.LastContext);
        Assert.Equal(@event.Context, session.LastContext);
    }

    private static string? GetField(BookingSession session, string fieldName) => fieldName switch
    {
        BookingFieldNames.Name => session.Name,
        BookingFieldNames.Email => session.Email,
        BookingFieldNames.Phone => session.Phone,
        BookingFieldNames.Message => session.Message,
        _ => throw new ArgumentOutOfRangeException(nameof(fieldName)),
    };
}
