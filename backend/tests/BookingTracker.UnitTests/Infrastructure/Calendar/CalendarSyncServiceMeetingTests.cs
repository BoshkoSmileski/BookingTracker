using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.Infrastructure.Calendar;
using BookingTracker.Infrastructure.Persistence;
using BookingTracker.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookingTracker.UnitTests.Infrastructure.Calendar;

/// <summary>
/// The Google Meet lifecycle, driven through the real CalendarSyncService
/// against a real InMemory-backed DbContext - the same call
/// BookingConflictCheckerTests makes, and for the same reason: what is being
/// asserted here is cross-table behaviour (a session, its page, a connection
/// and a synced-event row), which a mocked DbSet would only pretend to
/// exercise.
///
/// Only the provider and the token lookup are faked, because those are the two
/// things that would otherwise reach Google.
/// </summary>
public class CalendarSyncServiceMeetingTests
{
    private const string MeetUrl = "https://meet.google.com/abc-defg-hij";

    [Fact]
    public async Task SyncBookingCreated_OnAGoogleMeetPage_StoresTheMeetingLinkOnTheSession()
    {
        var (db, provider, sut, sessionId) = await ArrangeAsync(MeetingProviderType.GoogleMeet, MeetUrl);

        await sut.SyncBookingCreatedAsync(sessionId, default);

        Assert.True(provider.CreatedEvents.Single().RequestConference);

        var session = await db.BookingSessions.FirstAsync(s => s.Id == sessionId);
        Assert.Equal(MeetingProviderType.GoogleMeet, session.MeetingProvider);
        Assert.Equal(MeetUrl, session.MeetingUrl);
    }

    [Fact]
    public async Task SyncBookingCreated_OnAGoogleMeetPage_RecordsTheLinkAsAnEventSoRebuildCanReplayIt()
    {
        var (db, _, sut, sessionId) = await ArrangeAsync(MeetingProviderType.GoogleMeet, MeetUrl);

        await sut.SyncBookingCreatedAsync(sessionId, default);

        var @event = await db.BookingSessionEvents
            .SingleAsync(e => e.SessionId == sessionId && e.EventType == BookingEventType.MeetingLinkAssigned);
        Assert.Equal(nameof(MeetingProviderType.GoogleMeet), @event.FieldName);
        Assert.Equal(MeetUrl, @event.NewValue);
    }

    [Fact]
    public async Task SyncBookingCreated_OnAnInPersonPage_RequestsNoConferenceAndStoresNoLink()
    {
        // The link the provider *would* return is set deliberately: this proves
        // the absence comes from not asking, not from nothing being available.
        var (db, provider, sut, sessionId) = await ArrangeAsync(MeetingProviderType.None, MeetUrl);

        await sut.SyncBookingCreatedAsync(sessionId, default);

        Assert.False(provider.CreatedEvents.Single().RequestConference);

        var session = await db.BookingSessions.FirstAsync(s => s.Id == sessionId);
        Assert.Null(session.MeetingProvider);
        Assert.Null(session.MeetingUrl);
        Assert.DoesNotContain(
            await db.BookingSessionEvents.Where(e => e.SessionId == sessionId).ToListAsync(),
            e => e.EventType == BookingEventType.MeetingLinkAssigned);
    }

    [Fact]
    public async Task SyncBookingCreated_WhenGoogleReturnsNoConference_LeavesTheBookingWithoutAMeetingRatherThanFailing()
    {
        // Google can create the event and still decline (or defer) the
        // conference - a Workspace policy, or a createRequest still pending.
        var (db, _, sut, sessionId) = await ArrangeAsync(MeetingProviderType.GoogleMeet, meetingUrl: null);

        await sut.SyncBookingCreatedAsync(sessionId, default);

        var session = await db.BookingSessions.FirstAsync(s => s.Id == sessionId);
        Assert.Null(session.MeetingUrl);
        // The calendar event itself still exists - only the meeting is missing.
        Assert.True(await db.CalendarSyncedEvents.AnyAsync(e => e.BookingSessionId == sessionId));
    }

    [Fact]
    public async Task SyncBookingCreated_WhenGoogleIsUnavailable_ThrowsWithoutStoringAPartialMeeting()
    {
        // The service does NOT swallow this - the booking command handler's
        // best-effort try/catch is what protects the booking, and it can only
        // log a failure it is actually told about. What matters here is that
        // nothing half-written is left behind.
        var (db, provider, sut, sessionId) = await ArrangeAsync(MeetingProviderType.GoogleMeet, MeetUrl);
        provider.CreateEventException = new HttpRequestException("Google is unreachable");

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.SyncBookingCreatedAsync(sessionId, default));

        var session = await db.BookingSessions.FirstAsync(s => s.Id == sessionId);
        Assert.Null(session.MeetingUrl);
        Assert.Null(session.MeetingProvider);
        Assert.False(await db.CalendarSyncedEvents.AnyAsync(e => e.BookingSessionId == sessionId));
    }

    [Fact]
    public async Task SyncBookingCreated_WithNoCalendarConnection_LeavesTheBookingWithoutAMeeting()
    {
        var (db, _, sut, sessionId) = await ArrangeAsync(MeetingProviderType.GoogleMeet, MeetUrl, withConnection: false);

        await sut.SyncBookingCreatedAsync(sessionId, default);

        var session = await db.BookingSessions.FirstAsync(s => s.Id == sessionId);
        Assert.Null(session.MeetingUrl);
    }

    [Fact]
    public async Task SyncBookingRescheduled_KeepsTheSameMeetingAndUpdatesTheEventInPlace()
    {
        // The heart of "one booking = one meeting": a reschedule must move the
        // event without touching the link a guest already holds.
        var (db, provider, sut, sessionId) = await ArrangeAsync(MeetingProviderType.GoogleMeet, MeetUrl);
        await sut.SyncBookingCreatedAsync(sessionId, default);

        var session = await db.BookingSessions.FirstAsync(s => s.Id == sessionId);
        session.Reschedule(session.SelectedDate!.Value.AddDays(1), new TimeOnly(14, 0), BookingSessionScenarios.SampleContext);
        await db.SaveChangesAsync();

        // A different URL would be returned if anything asked for a new
        // conference - so an unchanged link here means nothing did.
        provider.MeetingUrlToReturn = "https://meet.google.com/zzz-zzzz-zzz";

        await sut.SyncBookingRescheduledAsync(sessionId, default);

        Assert.Single(provider.CreatedEvents);
        Assert.Single(provider.UpdatedEvents);

        var reloaded = await db.BookingSessions.FirstAsync(s => s.Id == sessionId);
        Assert.Equal(MeetUrl, reloaded.MeetingUrl);
        Assert.Single(await db.BookingSessionEvents
            .Where(e => e.SessionId == sessionId && e.EventType == BookingEventType.MeetingLinkAssigned).ToListAsync());
    }

    [Fact]
    public async Task SyncBookingRescheduled_ForABookingThatNeverHadAnEvent_CreatesOneAndGivesItAMeeting()
    {
        // The booking predates the calendar connection, so there is nothing to
        // update - the service creates the event now, which is the first moment
        // a meeting can exist for it.
        var (db, provider, sut, sessionId) = await ArrangeAsync(MeetingProviderType.GoogleMeet, MeetUrl);

        await sut.SyncBookingRescheduledAsync(sessionId, default);

        Assert.Single(provider.CreatedEvents);
        Assert.Empty(provider.UpdatedEvents);
        Assert.Equal(MeetUrl, (await db.BookingSessions.FirstAsync(s => s.Id == sessionId)).MeetingUrl);
    }

    [Fact]
    public async Task SyncBookingCancelled_DeletesTheEventAndKeepsTheLinkAsHistory()
    {
        // The Google event goes (taking the live Meet with it), but the booking
        // still records that it had one. The read side is what hides it: see
        // GetBookingByTokenQueryHandler, which only returns a link while the
        // booking is still manageable.
        var (db, provider, sut, sessionId) = await ArrangeAsync(MeetingProviderType.GoogleMeet, MeetUrl);
        await sut.SyncBookingCreatedAsync(sessionId, default);

        await sut.SyncBookingCancelledAsync(sessionId, default);

        Assert.Single(provider.DeletedEventIds);
        Assert.False(await db.CalendarSyncedEvents.AnyAsync(e => e.BookingSessionId == sessionId));
        Assert.Equal(MeetUrl, (await db.BookingSessions.FirstAsync(s => s.Id == sessionId)).MeetingUrl);
    }

    [Fact]
    public async Task SyncBookingCreated_RunTwice_DoesNotRecordASecondMeetingEvent()
    {
        // A retried or repeated sync must not make the log say the meeting
        // moved. AssignMeetingLink is idempotent for exactly this.
        var (db, _, sut, sessionId) = await ArrangeAsync(MeetingProviderType.GoogleMeet, MeetUrl);

        await sut.SyncBookingCreatedAsync(sessionId, default);
        await sut.SyncBookingCreatedAsync(sessionId, default);

        Assert.Single(await db.BookingSessionEvents
            .Where(e => e.SessionId == sessionId && e.EventType == BookingEventType.MeetingLinkAssigned).ToListAsync());
    }

    private static async Task<(BookingTrackerDbContext Db, FakeCalendarProvider Provider, CalendarSyncService Sut, Guid SessionId)> ArrangeAsync(
        MeetingProviderType meetingProvider, string? meetingUrl, bool withConnection = true)
    {
        var db = InMemoryDbContextFactory.Create();

        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id, meetingProvider: meetingProvider);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        db.WorkingSchedules.Add(TestEntities.CreateWorkingSchedule(organizer.Id));

        if (withConnection)
        {
            db.CalendarConnections.Add(CalendarConnection.Connect(
                organizer.Id, CalendarProviderType.Google, "organizer@example.com",
                "primary", "Primary", "enc-access", "enc-refresh", DateTime.UtcNow.AddHours(1)));
        }

        // A real submitted booking, driven through the same lifecycle the
        // wizard uses, rather than an entity poked into a Submitted state.
        var booking = BookingSessionScenarios.StartFillAndSubmit(page.Id);
        db.BookingSessions.Add(booking.Session);
        db.BookingSessionEvents.AddRange(booking.Events);
        await db.SaveChangesAsync();

        var provider = new FakeCalendarProvider { MeetingUrlToReturn = meetingUrl };
        var sut = new CalendarSyncService(
            db,
            new FakeCalendarConnectionService(),
            [provider],
            new FakeFrontendLinkBuilder(),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<CalendarSyncService>.Instance);

        return (db, provider, sut, booking.Session.Id);
    }
}
