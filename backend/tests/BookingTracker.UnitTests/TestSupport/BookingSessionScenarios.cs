using BookingTracker.Domain.Common;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.Domain.ValueObjects;

namespace BookingTracker.UnitTests.TestSupport;

/// <summary>
/// Drives a BookingSession through realistic multi-step lifecycles the same
/// way the real booking wizard does (one call per visitor interaction, never
/// setting fields directly), collecting every emitted event along the way.
/// Shared by BookingSessionTests and the event-sourcing/Rebuild tests so both
/// exercise the exact same event sequences instead of each hand-rolling
/// slightly different ones.
/// </summary>
public static class BookingSessionScenarios
{
    public static readonly ClientContext SampleContext = new("203.0.113.10", "test-agent/1.0");

    public sealed record Result(BookingSession Session, List<BookingSessionEvent> Events);

    /// <summary>Start -> fill in name/email/phone/message -> pick a date/time -> submit. The common happy path.</summary>
    public static Result StartFillAndSubmit(
        Guid bookingPageId,
        string name = "Jane Doe",
        string email = "jane@example.com",
        string phone = "+1 555 0100",
        string message = "Looking forward to it",
        int year = 2026, int month = 8, int day = 10, int hour = 9, int minute = 0)
    {
        var events = new List<BookingSessionEvent>();
        var seq = 0;

        var (session, startEvent) = BookingSession.Start(bookingPageId, SampleContext);
        events.Add(startEvent);

        events.Add(session.ChangeField(BookingFieldNames.Name, name, ++seq, SampleContext));
        events.Add(session.ChangeField(BookingFieldNames.Email, email, ++seq, SampleContext));
        events.Add(session.ChangeField(BookingFieldNames.Phone, phone, ++seq, SampleContext));
        events.Add(session.ChangeField(BookingFieldNames.Message, message, ++seq, SampleContext));

        var date = new DateOnly(year, month, day);
        var time = new TimeOnly(hour, minute);
        events.Add(session.SelectDate(date, ++seq, SampleContext));
        events.Add(session.SelectTime(time, ++seq, SampleContext));

        events.Add(session.Submit(++seq, SampleContext));

        return new Result(session, events);
    }

    /// <summary>
    /// Happy path plus answers to the organizer's custom fields, reported the
    /// way the wizard reports them: ordinary ChangeField calls whose field name
    /// is BookingFieldNames.ForCustomField(id), interleaved with the built-in
    /// fields rather than batched at the end.
    /// </summary>
    public static Result StartFillAnswerAndSubmit(
        Guid bookingPageId,
        params (Guid FieldId, string Value)[] answers)
    {
        var events = new List<BookingSessionEvent>();
        var seq = 0;

        var (session, startEvent) = BookingSession.Start(bookingPageId, SampleContext);
        events.Add(startEvent);

        events.Add(session.ChangeField(BookingFieldNames.Name, "Jane Doe", ++seq, SampleContext));
        events.Add(session.ChangeField(BookingFieldNames.Email, "jane@example.com", ++seq, SampleContext));

        foreach (var (fieldId, value) in answers)
        {
            events.Add(session.ChangeField(BookingFieldNames.ForCustomField(fieldId), value, ++seq, SampleContext));
        }

        events.Add(session.SelectDate(new DateOnly(2026, 8, 10), ++seq, SampleContext));
        events.Add(session.SelectTime(new TimeOnly(9, 0), ++seq, SampleContext));
        events.Add(session.Submit(++seq, SampleContext));

        return new Result(session, events);
    }

    /// <summary>Happy path, then cancelled by the customer.</summary>
    public static Result SubmitThenCancel(Guid bookingPageId, string reason = "Can't make it anymore")
    {
        var result = StartFillAndSubmit(bookingPageId);
        result.Events.Add(result.Session.Cancel(CancelledByType.Customer, reason, SampleContext));
        return result;
    }

    /// <summary>Happy path, then rescheduled to a new date/time (optionally more than once).</summary>
    public static Result SubmitThenReschedule(Guid bookingPageId, params (int Year, int Month, int Day, int Hour, int Minute)[] reschedules)
    {
        var result = StartFillAndSubmit(bookingPageId);

        foreach (var r in reschedules)
        {
            var newDate = new DateOnly(r.Year, r.Month, r.Day);
            var newTime = new TimeOnly(r.Hour, r.Minute);
            result.Events.Add(result.Session.Reschedule(newDate, newTime, SampleContext));
        }

        return result;
    }
}
