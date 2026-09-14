using BookingTracker.Domain.Common;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.Domain.Exceptions;
using BookingTracker.UnitTests.TestSupport;

namespace BookingTracker.UnitTests.Domain.Entities;

/// <summary>
/// The meeting half of the booking aggregate: assigning a link, the invariants
/// that guard it, and - the point of modelling it as an event at all - that
/// Rebuild reconstructs it from the log with no special handling.
/// </summary>
public class BookingSessionMeetingTests
{
    private const string MeetUrl = "https://meet.google.com/abc-defg-hij";

    [Fact]
    public void AssignMeetingLink_SetsTheProviderAndUrl()
    {
        var session = BookingSessionScenarios.StartFillAndSubmit(Guid.NewGuid()).Session;

        var @event = session.AssignMeetingLink(MeetingProviderType.GoogleMeet, MeetUrl);

        Assert.NotNull(@event);
        Assert.Equal(BookingEventType.MeetingLinkAssigned, @event.EventType);
        Assert.Equal(MeetingProviderType.GoogleMeet, session.MeetingProvider);
        Assert.Equal(MeetUrl, session.MeetingUrl);
    }

    [Fact]
    public void AssignMeetingLink_WithTheLinkItAlreadyHas_IsANoOpAndEmitsNothing()
    {
        // One booking is one meeting - a repeated sync must not append a second
        // event saying the link changed when it did not.
        var session = BookingSessionScenarios.StartFillAndSubmit(Guid.NewGuid()).Session;
        session.AssignMeetingLink(MeetingProviderType.GoogleMeet, MeetUrl);
        var sequenceAfterFirst = session.LastClientSequenceNumber;

        var second = session.AssignMeetingLink(MeetingProviderType.GoogleMeet, MeetUrl);

        Assert.Null(second);
        Assert.Equal(sequenceAfterFirst, session.LastClientSequenceNumber);
        Assert.Equal(MeetUrl, session.MeetingUrl);
    }

    [Fact]
    public void AssignMeetingLink_WithADifferentLink_RecordsTheChange()
    {
        // The real case: a booking that had no calendar event gets one later,
        // so a link genuinely appears where there was none.
        var session = BookingSessionScenarios.StartFillAndSubmit(Guid.NewGuid()).Session;
        session.AssignMeetingLink(MeetingProviderType.GoogleMeet, MeetUrl);

        var second = session.AssignMeetingLink(MeetingProviderType.GoogleMeet, "https://meet.google.com/xyz-wxyz-abc");

        Assert.NotNull(second);
        Assert.Equal("https://meet.google.com/xyz-wxyz-abc", session.MeetingUrl);
    }

    [Fact]
    public void AssignMeetingLink_WithNoneProvider_Throws()
    {
        var session = BookingSessionScenarios.StartFillAndSubmit(Guid.NewGuid()).Session;

        Assert.Throws<DomainException>(() => session.AssignMeetingLink(MeetingProviderType.None, MeetUrl));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AssignMeetingLink_WithAnEmptyUrl_Throws(string url)
    {
        var session = BookingSessionScenarios.StartFillAndSubmit(Guid.NewGuid()).Session;

        Assert.Throws<DomainException>(() => session.AssignMeetingLink(MeetingProviderType.GoogleMeet, url));
    }

    [Fact]
    public void AssignMeetingLink_OverTheColumnLimit_ThrowsRatherThanTruncatingAtTheDatabase()
    {
        // A column limit must never be the thing that
        // rejects a value. This one has no validator in front of it (nothing
        // user-supplied reaches it), so the domain guard is the front line.
        var session = BookingSessionScenarios.StartFillAndSubmit(Guid.NewGuid()).Session;
        var tooLong = "https://meet.google.com/" + new string('a', BookingFieldLimits.MeetingUrlMaxLength);

        Assert.Throws<DomainException>(() => session.AssignMeetingLink(MeetingProviderType.GoogleMeet, tooLong));
    }

    [Fact]
    public void ABookingWithNoMeeting_HasNoProviderAndNoUrl()
    {
        var session = BookingSessionScenarios.StartFillAndSubmit(Guid.NewGuid()).Session;

        Assert.Null(session.MeetingProvider);
        Assert.Null(session.MeetingUrl);
    }

    [Fact]
    public void Rebuild_ReconstructsTheMeetingFromTheEventLogAlone()
    {
        // The claim the whole design rests on: the projection's meeting columns
        // hold nothing that is not already derivable from the log.
        var pageId = Guid.NewGuid();
        var result = BookingSessionScenarios.StartFillAndSubmit(pageId);
        result.Events.Add(result.Session.AssignMeetingLink(MeetingProviderType.GoogleMeet, MeetUrl)!);

        var rebuilt = BookingSession.Rebuild(pageId, result.Events.OrderBy(e => e.ClientSequenceNumber));

        Assert.Equal(result.Session.MeetingProvider, rebuilt.MeetingProvider);
        Assert.Equal(result.Session.MeetingUrl, rebuilt.MeetingUrl);
    }

    [Fact]
    public void Rebuild_AcrossAFullLifecycle_KeepsTheMeetingThroughRescheduleAndCancellation()
    {
        // A cancelled booking keeps its meeting as history: the Google event is
        // deleted, but the log still says a meeting existed, and the read side
        // is what decides whether to offer it (see GetBookingByTokenQueryHandler).
        var pageId = Guid.NewGuid();
        var result = BookingSessionScenarios.StartFillAndSubmit(pageId);
        result.Events.Add(result.Session.AssignMeetingLink(MeetingProviderType.GoogleMeet, MeetUrl)!);
        result.Events.Add(result.Session.Reschedule(new DateOnly(2026, 9, 1), new TimeOnly(11, 0), BookingSessionScenarios.SampleContext));
        result.Events.Add(result.Session.Cancel(CancelledByType.Organizer, "Something came up", BookingSessionScenarios.SampleContext));

        var rebuilt = BookingSession.Rebuild(pageId, result.Events.OrderBy(e => e.ClientSequenceNumber));

        Assert.Equal(BookingSessionStatus.Cancelled, rebuilt.Status);
        Assert.Equal(MeetingProviderType.GoogleMeet, rebuilt.MeetingProvider);
        Assert.Equal(MeetUrl, rebuilt.MeetingUrl);
    }

    [Fact]
    public void MeetingUrlLimit_LeavesRoomForARealGoogleMeetLink()
    {
        // Guards the constant itself: a Meet URL that does not fit would be an
        // SQL truncation on every booking, which is precisely what
        // BookingFieldLimits exists to prevent.
        Assert.True(MeetUrl.Length < BookingFieldLimits.MeetingUrlMaxLength);
    }
}
