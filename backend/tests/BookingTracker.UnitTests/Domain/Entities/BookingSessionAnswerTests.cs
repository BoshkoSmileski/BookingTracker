using BookingTracker.Domain.Common;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.Domain.Exceptions;
using BookingTracker.UnitTests.TestSupport;

namespace BookingTracker.UnitTests.Domain.Entities;

/// <summary>
/// The visitor's half of custom booking questions: answering them.
///
/// The claim under test throughout is that an answer is not a new kind of thing
/// - it is an ordinary FieldChanged event with a different FieldName, which is
/// why the feature needed no new BookingEventType. Rebuild's ability to
/// reconstruct answers follows from that and is proved separately in
/// BookingSessionEventSourcingTests.
/// </summary>
public class BookingSessionAnswerTests
{
    private static readonly Guid PageId = Guid.NewGuid();
    private static readonly Guid FieldId = Guid.NewGuid();

    private static BookingSession NewSession() => BookingSession.Start(PageId, BookingSessionScenarios.SampleContext).Session;

    [Fact]
    public void ChangeField_WithACustomFieldName_RecordsTheAnswer()
    {
        var session = NewSession();

        session.ChangeField(BookingFieldNames.ForCustomField(FieldId), "Acme Ltd", 1, BookingSessionScenarios.SampleContext);

        var answer = Assert.Single(session.Answers);
        Assert.Equal(FieldId, answer.BookingFormFieldId);
        Assert.Equal("Acme Ltd", answer.Value);
    }

    [Fact]
    public void ChangeField_WithACustomFieldName_EmitsAnOrdinaryFieldChangedEvent()
    {
        var session = NewSession();

        var @event = session.ChangeField(
            BookingFieldNames.ForCustomField(FieldId), "Acme Ltd", 1, BookingSessionScenarios.SampleContext);

        Assert.Equal(BookingEventType.FieldChanged, @event.EventType);
        Assert.Equal($"custom:{FieldId:D}", @event.FieldName);
        Assert.Equal("Acme Ltd", @event.NewValue);
    }

    [Fact]
    public void ChangeField_Repeatedly_KeepsOneAnswerHoldingTheLatestValue()
    {
        // The wizard reports every keystroke, so this is the normal case, not an edge one.
        var session = NewSession();
        var name = BookingFieldNames.ForCustomField(FieldId);

        session.ChangeField(name, "A", 1, BookingSessionScenarios.SampleContext);
        session.ChangeField(name, "Ac", 2, BookingSessionScenarios.SampleContext);
        session.ChangeField(name, "Acme", 3, BookingSessionScenarios.SampleContext);

        Assert.Equal("Acme", Assert.Single(session.Answers).Value);
    }

    [Fact]
    public void ChangeField_CarriesThePreviousAnswerAsTheEventsOldValue()
    {
        var session = NewSession();
        var name = BookingFieldNames.ForCustomField(FieldId);
        session.ChangeField(name, "Acme", 1, BookingSessionScenarios.SampleContext);

        var @event = session.ChangeField(name, "Acme Ltd", 2, BookingSessionScenarios.SampleContext);

        Assert.Equal("Acme", @event.OldValue);
        Assert.Equal("Acme Ltd", @event.NewValue);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ChangeField_ClearingAnAnswer_RemovesItRatherThanStoringBlank(string? cleared)
    {
        // "Answered then erased" must end up indistinguishable from "never
        // answered", or a replay of the log would not match the projection.
        var session = NewSession();
        var name = BookingFieldNames.ForCustomField(FieldId);
        session.ChangeField(name, "Acme", 1, BookingSessionScenarios.SampleContext);

        session.ChangeField(name, cleared, 2, BookingSessionScenarios.SampleContext);

        Assert.Empty(session.Answers);
    }

    [Fact]
    public void ChangeField_TracksSeveralCustomFieldsIndependently()
    {
        var session = NewSession();
        var otherFieldId = Guid.NewGuid();

        session.ChangeField(BookingFieldNames.ForCustomField(FieldId), "Acme", 1, BookingSessionScenarios.SampleContext);
        session.ChangeField(BookingFieldNames.ForCustomField(otherFieldId), "Pricing", 2, BookingSessionScenarios.SampleContext);

        Assert.Equal(2, session.Answers.Count);
        Assert.Equal("Acme", session.Answers.Single(a => a.BookingFormFieldId == FieldId).Value);
        Assert.Equal("Pricing", session.Answers.Single(a => a.BookingFormFieldId == otherFieldId).Value);
    }

    [Fact]
    public void ChangeField_RejectsAnAnswerOverTheSharedColumnLimit()
    {
        var session = NewSession();
        var tooLong = new string('x', BookingFieldLimits.CustomAnswerMaxLength + 1);

        Assert.Throws<DomainException>(() => session.ChangeField(
            BookingFieldNames.ForCustomField(FieldId), tooLong, 1, BookingSessionScenarios.SampleContext));
    }

    [Fact]
    public void ChangeField_AcceptsAnAnswerExactlyAtTheLimit()
    {
        var session = NewSession();
        var atLimit = new string('x', BookingFieldLimits.CustomAnswerMaxLength);

        session.ChangeField(BookingFieldNames.ForCustomField(FieldId), atLimit, 1, BookingSessionScenarios.SampleContext);

        Assert.Equal(atLimit, Assert.Single(session.Answers).Value);
    }

    [Fact]
    public void ChangeField_StillRejectsAFieldNameThatIsNeitherBuiltInNorCustom()
    {
        // Opening FieldName up to custom fields must not open it up to anything.
        var session = NewSession();

        Assert.Throws<DomainException>(() => session.ChangeField("Nonsense", "x", 1, BookingSessionScenarios.SampleContext));
        Assert.Throws<DomainException>(() => session.ChangeField("custom:not-a-guid", "x", 2, BookingSessionScenarios.SampleContext));
    }

    [Fact]
    public void ChangeField_RejectsAnAnswerOnceTheSessionIsNoLongerActive()
    {
        var result = BookingSessionScenarios.StartFillAndSubmit(PageId);

        Assert.Throws<DomainException>(() => result.Session.ChangeField(
            BookingFieldNames.ForCustomField(FieldId), "Too late", 99, BookingSessionScenarios.SampleContext));
    }

    [Fact]
    public void TryGetCustomFieldId_RoundTripsAndRejectsEverythingElse()
    {
        Assert.Equal(FieldId, BookingFieldNames.TryGetCustomFieldId(BookingFieldNames.ForCustomField(FieldId)));
        Assert.Null(BookingFieldNames.TryGetCustomFieldId(BookingFieldNames.Name));
        Assert.Null(BookingFieldNames.TryGetCustomFieldId("custom:"));
        Assert.Null(BookingFieldNames.TryGetCustomFieldId(null));
    }

    [Fact]
    public void CustomFieldName_FitsTheColumnThatStoresIt()
    {
        // The whole convention depends on this: a field name longer than the
        // BookingSessionEvents.FieldName column would be an SQL truncation, the
        // exact failure mode BookingFieldLimits exists to prevent.
        Assert.True(BookingFieldNames.ForCustomField(Guid.NewGuid()).Length <= BookingFieldLimits.EventFieldNameMaxLength);
    }
}
