using System.Net;
using System.Text.Json;
using BookingTracker.Domain.Enums;
using BookingTracker.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.IntegrationTests;

/// <summary>
/// BookingSession.PublicToken is a bearer credential: holding it is
/// sufficient to view, cancel and reschedule a booking, with no other
/// authentication. It is also written into the event log in plaintext -
/// BookingSubmitted carries the booking reference as OldValue and the token as
/// NewValue, because BookingSession.Apply restores both from there when
/// replaying (see BookingSessionEvent.BookingSubmitted).
///
/// That storage is fine on its own. What these tests pin is the boundary
/// between the log and the outside world: which surfaces hand out raw event
/// rows, and whether the token can therefore be obtained by someone who was
/// never given it.
///
/// These are security regression tests. A failure here is not a formatting
/// change - it means the token became reachable through a surface that does not
/// need to expose it.
/// </summary>
public class PublicTokenExposureTests : ApiTestBase
{
    private sealed record Booked(Guid SessionId, string PublicToken, TestData.Workspace Workspace);

    /// <summary>A real confirmed booking, made over HTTP exactly as a guest makes one.</summary>
    private async Task<Booked> ArrangeBookingAsync(string email = "organizer@example.com", string slug = "test-page")
    {
        var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(db, email: email, slug: slug));
        var date = TestData.NextBookableWeekday();
        var time = await BookingFlow.FirstAvailableTimeAsync(Client, slug, date);

        var sessionId = await BookingFlow.StartSessionAsync(Client, slug);
        await BookingFlow.FillAsync(Client, sessionId, date, time);
        var confirmation = await ReadJsonAsync(await BookingFlow.SubmitAsync(Client, sessionId));

        var token = confirmation.GetProperty("publicToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token), "The submit response should carry the guest's own token.");

        return new Booked(sessionId, token!, workspace);
    }

    /// <summary>Every OldValue/NewValue an event array carries, which is where a leaked token would be.</summary>
    private static IEnumerable<string> EventValues(JsonElement events)
    {
        foreach (var @event in events.EnumerateArray())
        {
            foreach (var property in new[] { "oldValue", "newValue", "fieldName" })
            {
                if (@event.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
                    yield return value.GetString()!;
            }
        }
    }

    /// <summary>
    /// Every string anywhere in a response, however deeply nested. Deliberately
    /// blunt: a capability test must not depend on knowing which property a
    /// future leak would arrive in - that assumption is exactly what let the
    /// let the token travel as an untyped NewValue past a DTO guard that was
    /// itself correct.
    /// </summary>
    private static IEnumerable<string> StringValues(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text)) yield return text;
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    foreach (var nested in StringValues(property.Value))
                        yield return nested;
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    foreach (var nested in StringValues(item))
                        yield return nested;
                break;
        }
    }

    // ---- D. Which surfaces intentionally return the token -------------------

    /// <summary>
    /// The one legitimate disclosure: the guest who just made the booking is
    /// handed their own credential, because managing the booking is what it is
    /// for. Pinned so the distinction between this and the leaks below is
    /// explicit rather than assumed.
    /// </summary>
    [Fact]
    public async Task TheSubmitResponseIntentionallyReturnsTheTokenToTheGuestWhoBooked()
    {
        var booking = await ArrangeBookingAsync();

        var stored = await WithDbAsync(db => db.BookingSessions
            .Where(s => s.Id == booking.SessionId)
            .Select(s => s.PublicToken)
            .SingleAsync());

        Assert.Equal(stored, booking.PublicToken);
    }

    /// <summary>
    /// The session projection endpoint is anonymous, so BookingSessionDto
    /// deliberately has no PublicToken - only BookingConfirmationDto does. This
    /// is the split the leak below bypasses.
    /// </summary>
    [Fact]
    public async Task TheAnonymousSessionEndpointDoesNotReturnTheToken()
    {
        var booking = await ArrangeBookingAsync();

        var session = await ReadJsonAsync(await Client.GetAsync($"/api/booking-sessions/{booking.SessionId}"));

        Assert.False(session.TryGetProperty("publicToken", out _));
        Assert.DoesNotContain(booking.PublicToken, session.ToString(), StringComparison.Ordinal);
    }

    /// <summary>The rebuild endpoint returns the same projection DTO, so it is clean for the same reason.</summary>
    [Fact]
    public async Task TheAnonymousRebuildEndpointDoesNotReturnTheToken()
    {
        var booking = await ArrangeBookingAsync();

        var rebuilt = await ReadJsonAsync(await Client.GetAsync($"/api/booking-sessions/{booking.SessionId}/rebuild"));

        Assert.False(rebuilt.TryGetProperty("publicToken", out _));
        Assert.DoesNotContain(booking.PublicToken, rebuilt.ToString(), StringComparison.Ordinal);
    }

    // ---- C. The guest/public surface ---------------------------------------

    /// <summary>
    /// THE ORIGINAL FINDING, now closed at the route rather than at the value.
    /// GET /api/booking-sessions/{id}/timeline was anonymous and returned raw
    /// event rows, so a caller holding only the session id - an identifier,
    /// never designed as a credential - could read the BookingSubmitted event's
    /// NewValue and obtain the booking's bearer token. The value was redacted
    /// first, then the route itself removed once it was shown to have no caller.
    ///
    /// The redaction is still in force and still pinned - see
    /// TheOwningOrganizersTimelineDoesNotCarryTheTokenEither, which asserts it
    /// on the authenticated route the app actually uses.
    /// </summary>
    [Fact]
    public async Task TheAnonymousTimelineRouteNoLongerExists()
    {
        var booking = await ArrangeBookingAsync();

        var response = await Client.GetAsync($"/api/booking-sessions/{booking.SessionId}/timeline");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// The consequence, stated as the capability rather than as a string match,
    /// and now over EVERY surface an anonymous caller holding a session id can
    /// still reach - not just the one that used to leak. Nothing readable
    /// without authentication may be usable to take over the booking.
    /// </summary>
    [Fact]
    public async Task AnAnonymousCallerWithOnlyASessionIdCannotTakeOverTheBooking()
    {
        var booking = await ArrangeBookingAsync();

        var candidates = new List<string>();
        foreach (var route in new[] { $"/api/booking-sessions/{booking.SessionId}",
                                      $"/api/booking-sessions/{booking.SessionId}/rebuild" })
        {
            var response = await Client.GetAsync(route);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            candidates.AddRange(StringValues(await ReadJsonAsync(response)));
        }

        Assert.NotEmpty(candidates);
        foreach (var candidate in candidates)
        {
            var response = await Client.GetAsync($"/api/bookings/{Uri.EscapeDataString(candidate)}");
            Assert.True(
                response.StatusCode is HttpStatusCode.NotFound,
                $"A value readable anonymously ('{candidate}') was accepted as a booking credential.");
        }
    }

    /// <summary>
    /// The booking reference is not a secret (it is a short support code, printed
    /// in emails), but it must not be a credential either - so the same event
    /// carrying both values is worth pinning from the other side.
    /// </summary>
    [Fact]
    public async Task TheBookingReferenceIsNotAcceptedAsACredential()
    {
        var booking = await ArrangeBookingAsync();
        var reference = await WithDbAsync(db => db.BookingSessions
            .Where(s => s.Id == booking.SessionId)
            .Select(s => s.BookingReference)
            .SingleAsync());

        var response = await Client.GetAsync($"/api/bookings/{reference}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- B. Cross-organizer -------------------------------------------------

    /// <summary>
    /// The organizer-scoped timeline is a different route into the same handler,
    /// and it does pass RequestingOrganizerId - so another organizer is refused
    /// before any event row is read.
    /// </summary>
    [Fact]
    public async Task AnotherOrganizerCannotReadTheTimelineAtAll()
    {
        var booking = await ArrangeBookingAsync(email: "alice@example.com", slug: "alice-page");
        var bob = await WithDbAsync(db => TestData.AddWorkspaceAsync(db, email: "bob@example.com", slug: "bob-page"));
        using var bobClient = ClientFor(bob.Organizer);

        var response = await bobClient.GetAsync(
            $"/api/organizer/booking-pages/{booking.Workspace.Page.Id}/sessions/{booking.SessionId}/timeline");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Bob substituting his own page id must not launder the ownership check either.</summary>
    [Fact]
    public async Task AnotherOrganizerCannotReadTheTimelineUsingTheirOwnPageId()
    {
        var booking = await ArrangeBookingAsync(email: "alice@example.com", slug: "alice-page");
        var bob = await WithDbAsync(db => TestData.AddWorkspaceAsync(db, email: "bob@example.com", slug: "bob-page"));
        using var bobClient = ClientFor(bob.Organizer);

        var response = await bobClient.GetAsync(
            $"/api/organizer/booking-pages/{bob.Page.Id}/sessions/{booking.SessionId}/timeline");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- A. The owning organizer -------------------------------------------

    /// <summary>
    /// The owning organizer legitimately reads the timeline, and can already
    /// cancel and reschedule this booking through their own authenticated
    /// endpoints - so the token grants them nothing new. It is still removed
    /// from what they are sent: a credential that reaches a screen, a log or a
    /// support screenshot has left the system, and nothing on the dashboard
    /// reads it. Least privilege, not a privilege boundary.
    /// </summary>
    [Fact]
    public async Task TheOwningOrganizersTimelineDoesNotCarryTheTokenEither()
    {
        var booking = await ArrangeBookingAsync();
        using var organizerClient = ClientFor(booking.Workspace.Organizer);

        var response = await organizerClient.GetAsync(
            $"/api/organizer/booking-pages/{booking.Workspace.Page.Id}/sessions/{booking.SessionId}/timeline");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(booking.PublicToken, EventValues(await ReadJsonAsync(response)));
    }

    /// <summary>
    /// The timeline must stay a timeline. Redacting one value cannot be allowed
    /// to become "return fewer events", or the event-sourcing view the dashboard
    /// exists to show would be quietly gutted.
    /// </summary>
    [Fact]
    public async Task TheTimelineStillReportsEveryEventIncludingTheSubmission()
    {
        var booking = await ArrangeBookingAsync();
        using var organizerClient = ClientFor(booking.Workspace.Organizer);

        // Read through the organizer route: since the removal of the anonymous
        // one, this is the only way the timeline is served at all - and it is the
        // route SessionDetailPage actually calls.
        var response = await organizerClient.GetAsync(
            $"/api/organizer/booking-pages/{booking.Workspace.Page.Id}/sessions/{booking.SessionId}/timeline");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var events = await ReadJsonAsync(response);

        var types = events.EnumerateArray().Select(e => e.GetProperty("eventType").GetString()).ToList();
        Assert.Contains("SessionStarted", types);
        Assert.Contains("FieldChanged", types);
        Assert.Contains("DateSelected", types);
        Assert.Contains("TimeSelected", types);
        Assert.Contains("BookingSubmitted", types);

        var submitted = events.EnumerateArray().Single(e => e.GetProperty("eventType").GetString() == "BookingSubmitted");
        var reference = await WithDbAsync(db => db.BookingSessions
            .Where(s => s.Id == booking.SessionId)
            .Select(s => s.BookingReference)
            .SingleAsync());
        Assert.Equal(reference, submitted.GetProperty("oldValue").GetString());
    }

    // ---- E. Rebuild ---------------------------------------------------------

    /// <summary>
    /// The event-sourcing contract this whole project rests on: the projection is
    /// a pure function of the log. Rebuild reads the token from the stored event
    /// row, so whatever is done about the exposure above must not touch the row
    /// itself - only what the API hands out.
    /// </summary>
    [Fact]
    public async Task RebuildStillReconstructsTheSessionFromTheLog()
    {
        var booking = await ArrangeBookingAsync();

        var projection = await ReadJsonAsync(await Client.GetAsync($"/api/booking-sessions/{booking.SessionId}"));
        var rebuilt = await ReadJsonAsync(await Client.GetAsync($"/api/booking-sessions/{booking.SessionId}/rebuild"));

        Assert.Equal(projection.GetProperty("status").GetString(), rebuilt.GetProperty("status").GetString());
        Assert.Equal(projection.GetProperty("name").GetString(), rebuilt.GetProperty("name").GetString());
        Assert.Equal(projection.GetProperty("email").GetString(), rebuilt.GetProperty("email").GetString());
        Assert.Equal(BookingSessionStatus.Submitted.ToString(), rebuilt.GetProperty("status").GetString());
    }

    /// <summary>
    /// And the token specifically: replaying the log has to restore the exact
    /// value the projection holds, which is why the plaintext row cannot simply
    /// be removed. Asserted against the database, since neither DTO exposes it.
    /// </summary>
    [Fact]
    public async Task ReplayingTheLogRestoresTheExactStoredToken()
    {
        var booking = await ArrangeBookingAsync();

        var (storedToken, rebuiltToken) = await WithDbAsync(async db =>
        {
            var session = await db.BookingSessions.AsNoTracking().SingleAsync(s => s.Id == booking.SessionId);
            var events = await db.BookingSessionEvents.AsNoTracking()
                .Where(e => e.SessionId == booking.SessionId)
                .OrderBy(e => e.ClientSequenceNumber).ThenBy(e => e.Id)
                .ToListAsync();

            var rebuilt = Domain.Entities.BookingSession.Rebuild(session.BookingPageId, events);
            return (session.PublicToken, rebuilt.PublicToken);
        });

        Assert.Equal(booking.PublicToken, storedToken);
        Assert.Equal(storedToken, rebuiltToken);
    }
}
