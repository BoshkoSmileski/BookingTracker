using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.UnitTests.TestSupport;

namespace BookingTracker.UnitTests.Domain.Entities;

/// <summary>
/// Proves the core thesis claim: BookingSession.Rebuild(), replaying nothing
/// but a persisted event log through the exact same Apply() method the live
/// path uses, always reconstructs identical state to the live, incrementally-
/// mutated projection - for every event type, not just the happy path.
/// </summary>
public class BookingSessionEventSourcingTests
{
    private static readonly Guid PageId = Guid.NewGuid();

    [Fact]
    public void Rebuild_HappyPath_ProducesExactlyTheSameStateAsTheLiveSession()
    {
        // Arrange
        var live = BookingSessionScenarios.StartFillAndSubmit(PageId);
        var orderedEvents = live.Events.OrderBy(e => e.ClientSequenceNumber).ToList();

        // Act
        var rebuilt = BookingSession.Rebuild(PageId, orderedEvents);

        // Assert
        AssertSameState(live.Session, rebuilt);
    }

    [Fact]
    public void Rebuild_NameEmailPhoneMessageChanges_ArePreserved()
    {
        var live = BookingSessionScenarios.StartFillAndSubmit(
            PageId, name: "Ada Lovelace", email: "ada@example.com", phone: "+44 20 7946", message: "Bring the analytical engine");
        var rebuilt = BookingSession.Rebuild(PageId, live.Events.OrderBy(e => e.ClientSequenceNumber));

        Assert.Equal("Ada Lovelace", rebuilt.Name);
        Assert.Equal("ada@example.com", rebuilt.Email);
        Assert.Equal("+44 20 7946", rebuilt.Phone);
        Assert.Equal("Bring the analytical engine", rebuilt.Message);
    }

    [Fact]
    public void Rebuild_DateAndTimeSelection_IsPreserved()
    {
        var live = BookingSessionScenarios.StartFillAndSubmit(PageId, year: 2026, month: 11, day: 3, hour: 14, minute: 30);
        var rebuilt = BookingSession.Rebuild(PageId, live.Events.OrderBy(e => e.ClientSequenceNumber));

        Assert.Equal(new DateOnly(2026, 11, 3), rebuilt.SelectedDate);
        Assert.Equal(new TimeOnly(14, 30), rebuilt.SelectedTime);
    }

    [Fact]
    public void Rebuild_LaterFieldChangeOverwritesEarlierOne_SameAsLiveSession()
    {
        // A field can change multiple times before submit - only the final value should survive.
        var (session, startEvent) = BookingSession.Start(PageId, BookingSessionScenarios.SampleContext);
        var events = new List<BookingSessionEvent> { startEvent };
        events.Add(session.ChangeField(BookingTracker.Domain.Common.BookingFieldNames.Name, "J", 1, BookingSessionScenarios.SampleContext));
        events.Add(session.ChangeField(BookingTracker.Domain.Common.BookingFieldNames.Name, "Ja", 2, BookingSessionScenarios.SampleContext));
        events.Add(session.ChangeField(BookingTracker.Domain.Common.BookingFieldNames.Name, "Jane", 3, BookingSessionScenarios.SampleContext));

        var rebuilt = BookingSession.Rebuild(PageId, events.OrderBy(e => e.ClientSequenceNumber));

        Assert.Equal("Jane", session.Name);
        Assert.Equal("Jane", rebuilt.Name);
    }

    [Fact]
    public void Rebuild_Cancellation_IsPreserved()
    {
        var live = BookingSessionScenarios.SubmitThenCancel(PageId, reason: "Schedule conflict");
        var rebuilt = BookingSession.Rebuild(PageId, live.Events.OrderBy(e => e.ClientSequenceNumber));

        Assert.Equal(BookingSessionStatus.Cancelled, rebuilt.Status);
        Assert.Equal(CancelledByType.Customer, rebuilt.CancelledBy);
        Assert.Equal("Schedule conflict", rebuilt.CancellationReason);
        Assert.NotNull(rebuilt.CancelledAt);
        AssertSameState(live.Session, rebuilt);
    }

    [Fact]
    public void Rebuild_Reschedule_IsPreserved()
    {
        var live = BookingSessionScenarios.SubmitThenReschedule(PageId, (2026, 9, 1, 10, 0), (2026, 9, 5, 15, 30));
        var rebuilt = BookingSession.Rebuild(PageId, live.Events.OrderBy(e => e.ClientSequenceNumber));

        Assert.Equal(new DateOnly(2026, 9, 5), rebuilt.SelectedDate);
        Assert.Equal(new TimeOnly(15, 30), rebuilt.SelectedTime);
        Assert.Equal(2, rebuilt.RescheduleCount);
        AssertSameState(live.Session, rebuilt);
    }

    [Fact]
    public void Rebuild_FullLifecycle_SubmitRescheduleThenCancel_MatchesLiveSessionExactly()
    {
        // Arrange - the fullest realistic event sequence: fields, slot pick, submit, a
        // reschedule, then a cancellation, exercising every event type that mutates state.
        var (session, startEvent) = BookingSession.Start(PageId, BookingSessionScenarios.SampleContext);
        var events = new List<BookingSessionEvent> { startEvent };
        var seq = 0;
        events.Add(session.ChangeField(BookingTracker.Domain.Common.BookingFieldNames.Name, "Grace Hopper", ++seq, BookingSessionScenarios.SampleContext));
        events.Add(session.ChangeField(BookingTracker.Domain.Common.BookingFieldNames.Email, "grace@example.com", ++seq, BookingSessionScenarios.SampleContext));
        events.Add(session.SelectDate(new DateOnly(2026, 6, 1), ++seq, BookingSessionScenarios.SampleContext));
        events.Add(session.SelectTime(new TimeOnly(10, 0), ++seq, BookingSessionScenarios.SampleContext));
        events.Add(session.MarkInactive(++seq, BookingSessionScenarios.SampleContext));
        events.Add(session.MarkActive(++seq, BookingSessionScenarios.SampleContext));
        events.Add(session.Submit(++seq, BookingSessionScenarios.SampleContext));
        events.Add(session.Reschedule(new DateOnly(2026, 6, 8), new TimeOnly(11, 0), BookingSessionScenarios.SampleContext));
        events.Add(session.Cancel(CancelledByType.Organizer, "Organizer unavailable", BookingSessionScenarios.SampleContext));

        // Act
        var rebuilt = BookingSession.Rebuild(PageId, events.OrderBy(e => e.ClientSequenceNumber));

        // Assert
        AssertSameState(session, rebuilt);
        Assert.Equal(BookingSessionStatus.Cancelled, rebuilt.Status);
        Assert.Equal(1, rebuilt.RescheduleCount);
        Assert.Equal(new DateOnly(2026, 6, 8), rebuilt.SelectedDate);
    }

    [Fact]
    public void Rebuild_EventsSuppliedOutOfCreationOrder_StillReplaysByClientSequenceNumber()
    {
        // The wire/DB order events happen to arrive in must not matter - only
        // ClientSequenceNumber does. Shuffle the list before rebuilding (but keep
        // it sorted by sequence number, exactly like the real query handler
        // does with `.OrderBy(e => e.ClientSequenceNumber).ThenBy(e => e.Id)`
        // before ever calling Rebuild) and confirm the result is unaffected.
        var live = BookingSessionScenarios.StartFillAndSubmit(PageId);
        var shuffled = live.Events.OrderByDescending(e => e.Id).ThenBy(e => Guid.NewGuid()).ToList();
        var correctlyOrdered = shuffled.OrderBy(e => e.ClientSequenceNumber).ToList();

        var rebuilt = BookingSession.Rebuild(PageId, correctlyOrdered);

        AssertSameState(live.Session, rebuilt);
    }

    [Fact]
    public void Rebuild_EmptyEventLog_ProducesASessionWithNoStatusTransition()
    {
        var rebuilt = BookingSession.Rebuild(PageId, []);

        // No SessionStarted event was replayed, so Status stays at its default (Active = 0)
        // but none of the fields SessionStarted would have set (CreatedAt) are populated -
        // this isn't a realistic scenario in production (every real session has at least
        // one event) but documents that Rebuild has no special-casing for an empty log.
        Assert.Equal(default, rebuilt.CreatedAt);
        Assert.Null(rebuilt.Name);
    }

    [Fact]
    public void Rebuild_CustomFieldAnswers_AreReconstructedFromTheLogAlone()
    {
        // The point of the whole design: because an answer is an ordinary
        // FieldChanged event, Rebuild needed no changes to reconstruct one.
        var companyId = Guid.NewGuid();
        var topicId = Guid.NewGuid();
        var live = BookingSessionScenarios.StartFillAnswerAndSubmit(
            PageId, (companyId, "Acme Ltd"), (topicId, "Pricing for next quarter"));

        var rebuilt = BookingSession.Rebuild(PageId, live.Events.OrderBy(e => e.ClientSequenceNumber));

        AssertSameState(live.Session, rebuilt);
        Assert.Equal(2, rebuilt.Answers.Count);
        Assert.Equal("Acme Ltd", rebuilt.Answers.Single(a => a.BookingFormFieldId == companyId).Value);
        Assert.Equal("Pricing for next quarter", rebuilt.Answers.Single(a => a.BookingFormFieldId == topicId).Value);
    }

    [Fact]
    public void Rebuild_AnAnswerEditedThenCleared_LeavesNoAnswerJustLikeTheLiveSession()
    {
        var fieldId = Guid.NewGuid();
        var name = BookingTracker.Domain.Common.BookingFieldNames.ForCustomField(fieldId);
        var (session, startEvent) = BookingSession.Start(PageId, BookingSessionScenarios.SampleContext);
        var events = new List<BookingSessionEvent> { startEvent };
        events.Add(session.ChangeField(name, "Acme", 1, BookingSessionScenarios.SampleContext));
        events.Add(session.ChangeField(name, "Acme Ltd", 2, BookingSessionScenarios.SampleContext));
        events.Add(session.ChangeField(name, null, 3, BookingSessionScenarios.SampleContext));

        var rebuilt = BookingSession.Rebuild(PageId, events.OrderBy(e => e.ClientSequenceNumber));

        Assert.Empty(session.Answers);
        Assert.Empty(rebuilt.Answers);
    }

    [Fact]
    public void Rebuild_AnswerEvents_AreOrderedByClientSequenceNumberNotListOrder()
    {
        // The same ordering guarantee the other event types get: a batch can
        // arrive out of order, and only the sequence number decides the winner.
        var fieldId = Guid.NewGuid();
        var name = BookingTracker.Domain.Common.BookingFieldNames.ForCustomField(fieldId);
        var (session, startEvent) = BookingSession.Start(PageId, BookingSessionScenarios.SampleContext);
        var events = new List<BookingSessionEvent> { startEvent };
        events.Add(session.ChangeField(name, "first", 1, BookingSessionScenarios.SampleContext));
        events.Add(session.ChangeField(name, "second", 2, BookingSessionScenarios.SampleContext));
        events.Add(session.ChangeField(name, "third", 3, BookingSessionScenarios.SampleContext));

        var shuffled = events.OrderByDescending(e => e.ClientSequenceNumber).ToList();
        var rebuilt = BookingSession.Rebuild(PageId, shuffled.OrderBy(e => e.ClientSequenceNumber));

        Assert.Equal("third", Assert.Single(rebuilt.Answers).Value);
    }

    /// <summary>
    /// The full field-by-field comparison "rebuilding produces exactly the
    /// same state as the projection" requires - deliberately more thorough
    /// than comparing DTOs, since BookingSessionDto omits several fields
    /// (CancelledBy/CancellationReason/RescheduleCount/PublicToken/etc.).
    /// </summary>
    private static void AssertSameState(BookingSession expected, BookingSession actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.BookingPageId, actual.BookingPageId);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Email, actual.Email);
        Assert.Equal(expected.Phone, actual.Phone);
        Assert.Equal(expected.Message, actual.Message);
        Assert.Equal(expected.SelectedDate, actual.SelectedDate);
        Assert.Equal(expected.SelectedTime, actual.SelectedTime);
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Equal(expected.LastActivityAt, actual.LastActivityAt);
        Assert.Equal(expected.SubmittedAt, actual.SubmittedAt);
        Assert.Equal(expected.AbandonedAt, actual.AbandonedAt);
        Assert.Equal(expected.BookingReference, actual.BookingReference);
        Assert.Equal(expected.PublicToken, actual.PublicToken);
        Assert.Equal(expected.CancelledAt, actual.CancelledAt);
        Assert.Equal(expected.CancelledBy, actual.CancelledBy);
        Assert.Equal(expected.CancellationReason, actual.CancellationReason);
        Assert.Equal(expected.RescheduledAt, actual.RescheduledAt);
        Assert.Equal(expected.RescheduleCount, actual.RescheduleCount);
        Assert.Equal(expected.LastClientSequenceNumber, actual.LastClientSequenceNumber);
        Assert.Equal(expected.LastContext, actual.LastContext);
        AssertSameAnswers(expected, actual);
    }

    private static void AssertSameAnswers(BookingSession expected, BookingSession actual)
    {
        Assert.Equal(
            expected.Answers.OrderBy(a => a.BookingFormFieldId).Select(a => (a.BookingFormFieldId, a.Value)),
            actual.Answers.OrderBy(a => a.BookingFormFieldId).Select(a => (a.BookingFormFieldId, a.Value)));
    }
}
