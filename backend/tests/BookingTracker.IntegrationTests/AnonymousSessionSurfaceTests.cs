using System.Net;
using System.Text.Json;
using BookingTracker.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.IntegrationTests;

/// <summary>
/// An earlier pass closed the credential half of the anonymous event-log
/// exposure and left the PII half open. This suite is the audit's conclusion,
/// turned into a contract.
///
/// The finding was not "PII is reachable anonymously" - some of it has to be.
/// GET /api/booking-sessions/{id} is how the wizard RESUMES a half-filled
/// booking after a reload (useBookingSessionTracker keeps the session id in
/// sessionStorage), and the guest has no credential at that point, because the
/// PublicToken does not exist until submit. So the guest's own name, email,
/// phone, message and answers are returned to the guest's own browser by design.
///
/// The finding was that TWO more anonymous surfaces existed on top of that one,
/// and only one of them added anything:
///
///   /rebuild   returns the SAME BookingSessionDto, from the same mapper. Zero
///              marginal disclosure - measured here, not assumed, so a future
///              change that widens it fails a test instead of going unnoticed.
///
///   /timeline  returned raw event rows: every intermediate keystroke as its own
///              FieldChanged, the client IP and User-Agent on every event, the
///              booking reference, and the recipient of every email sent. It had
///              ZERO callers - the frontend, the e2e suite and the backend all
///              read the timeline through the organizer route - so it was
///              removed rather than authorized, which would only have duplicated
///              a route that already exists.
///
///
/// </summary>
public class AnonymousSessionSurfaceTests : ApiTestBase
{
    private sealed record Booked(Guid SessionId, TestData.Workspace Workspace);

    private const string GuestName = "Jane Doe";
    private const string GuestEmail = "jane@example.com";
    private const string GuestPhone = "+389 70 123 456";

    /// <summary>Deliberately identifiable: every value here is something a leak would have to carry.</summary>
    private const string GuestMessage = "Account 55512345, please call before the meeting";

    private async Task<Booked> ArrangeBookingAsync(string email = "organizer@example.com", string slug = "test-page")
    {
        var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(db, email: email, slug: slug));
        var date = TestData.NextBookableWeekday();
        var time = await BookingFlow.FirstAvailableTimeAsync(Client, slug, date);

        var sessionId = await BookingFlow.StartSessionAsync(Client, slug);
        await BookingFlow.FillAsync(
            Client, sessionId, date, time, GuestName, GuestEmail,
            extraEvents:
            [
                new BookingFlow.ClientEvent("FieldChanged", "Phone", GuestPhone, 0),
                // Two edits of one field, so "every keystroke is its own event"
                // is actually represented rather than merely described.
                new BookingFlow.ClientEvent("FieldChanged", "Message", "Account 555", 0),
                new BookingFlow.ClientEvent("FieldChanged", "Message", GuestMessage, 0),
            ]);
        (await BookingFlow.SubmitAsync(Client, sessionId)).EnsureSuccessStatusCode();

        return new Booked(sessionId, workspace);
    }

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
                    foreach (var nested in StringValues(property.Value)) yield return nested;
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    foreach (var nested in StringValues(item)) yield return nested;
                break;
        }
    }

    private static IEnumerable<string> PropertyNames(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) yield break;
        foreach (var property in element.EnumerateObject()) yield return property.Name;
    }

    // ---- The surface that is gone -------------------------------------------

    /// <summary>
    /// The route removal itself. Asserted on the exact path the old action was
    /// mapped at, so reintroducing it fails here rather than silently restoring
    /// the widest anonymous surface in the API.
    /// </summary>
    [Fact]
    public async Task TheAnonymousTimelineRouteIsGone()
    {
        var booking = await ArrangeBookingAsync();

        var response = await Client.GetAsync($"/api/booking-sessions/{booking.SessionId}/timeline");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// And it is gone for an authenticated caller too - the point is that the
    /// route does not exist, not that anonymous callers are singled out. An
    /// organizer reads the timeline through their own ownership-checked route.
    /// </summary>
    [Fact]
    public async Task TheAnonymousTimelineRouteIsGoneForOrganizersAsWell()
    {
        var booking = await ArrangeBookingAsync();
        using var organizerClient = ClientFor(booking.Workspace.Organizer);

        var removed = await organizerClient.GetAsync($"/api/booking-sessions/{booking.SessionId}/timeline");
        var supported = await organizerClient.GetAsync(
            $"/api/organizer/booking-pages/{booking.Workspace.Page.Id}/sessions/{booking.SessionId}/timeline");

        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, supported.StatusCode);
    }

    // ---- The surface that must stay -----------------------------------------

    /// <summary>
    /// The intentional disclosure, pinned so nobody "fixes" it and breaks resume.
    /// A guest reloading mid-booking gets their own half-filled form back, and
    /// that requires the values they typed.
    /// </summary>
    [Fact]
    public async Task TheAnonymousSessionEndpointIntentionallyReturnsTheGuestsOwnDetails()
    {
        var booking = await ArrangeBookingAsync();

        var session = await ReadJsonAsync(await Client.GetAsync($"/api/booking-sessions/{booking.SessionId}"));

        Assert.Equal(GuestName, session.GetProperty("name").GetString());
        Assert.Equal(GuestEmail, session.GetProperty("email").GetString());
        Assert.Equal(GuestPhone, session.GetProperty("phone").GetString());
        Assert.Equal(GuestMessage, session.GetProperty("message").GetString());
    }

    /// <summary>
    /// The whole justification for leaving /rebuild anonymous, stated as a
    /// measurement: it returns the same property set as the endpoint that cannot
    /// be closed. If a future change gives rebuild a field GET /{id} does not
    /// have, the "closing rebuild would reduce nothing" argument stops holding,
    /// and this test says so.
    /// </summary>
    [Fact]
    public async Task RebuildDisclosesExactlyWhatTheSessionEndpointAlreadyDoes()
    {
        var booking = await ArrangeBookingAsync();

        var session = await ReadJsonAsync(await Client.GetAsync($"/api/booking-sessions/{booking.SessionId}"));
        var rebuilt = await ReadJsonAsync(await Client.GetAsync($"/api/booking-sessions/{booking.SessionId}/rebuild"));

        Assert.Equal(PropertyNames(session).OrderBy(n => n), PropertyNames(rebuilt).OrderBy(n => n));
        Assert.Equal(
            StringValues(session).OrderBy(v => v, StringComparer.Ordinal),
            StringValues(rebuilt).OrderBy(v => v, StringComparer.Ordinal));
    }

    // ---- What no anonymous surface may carry --------------------------------

    /// <summary>
    /// The event log's operational metadata is the part that was never anyone's
    /// to read: the client IP and User-Agent are recorded on every event
    /// (measured at 1551 of 1821 rows in the development database), and nothing
    /// in the product renders either - not even the organizer's own timeline.
    /// </summary>
    [Fact]
    public async Task NoAnonymousSurfaceReturnsClientIpOrUserAgent()
    {
        var booking = await ArrangeBookingAsync();

        foreach (var route in new[] { $"/api/booking-sessions/{booking.SessionId}",
                                      $"/api/booking-sessions/{booking.SessionId}/rebuild" })
        {
            var body = await ReadJsonAsync(await Client.GetAsync(route));
            var names = PropertyNames(body).ToList();

            Assert.DoesNotContain("clientIp", names);
            Assert.DoesNotContain("userAgent", names);
        }
    }

    /// <summary>
    /// The other half of what the timeline added: the per-keystroke history. The
    /// projection holds only the FINAL value of each field, so an intermediate
    /// edit the guest typed and replaced must not be reachable anonymously.
    /// </summary>
    [Fact]
    public async Task NoAnonymousSurfaceReturnsSupersededKeystrokeValues()
    {
        var booking = await ArrangeBookingAsync();

        foreach (var route in new[] { $"/api/booking-sessions/{booking.SessionId}",
                                      $"/api/booking-sessions/{booking.SessionId}/rebuild" })
        {
            var values = StringValues(await ReadJsonAsync(await Client.GetAsync(route))).ToList();

            Assert.Contains(GuestMessage, values);
            Assert.DoesNotContain("Account 555", values);
        }
    }

    /// <summary>
    /// The booking reference is not a credential (TheBookingReferenceIsNotAccepted-
    /// AsACredential pins that), but it is a support code printed in emails and
    /// there is no reason for an anonymous session read to carry it. It rode the
    /// timeline as BookingSubmitted.OldValue; nothing anonymous returns it now.
    /// </summary>
    [Fact]
    public async Task NoAnonymousSurfaceReturnsTheBookingReference()
    {
        var booking = await ArrangeBookingAsync();
        var reference = await WithDbAsync(db => db.BookingSessions
            .Where(s => s.Id == booking.SessionId)
            .Select(s => s.BookingReference)
            .SingleAsync());

        Assert.False(string.IsNullOrWhiteSpace(reference));
        foreach (var route in new[] { $"/api/booking-sessions/{booking.SessionId}",
                                      $"/api/booking-sessions/{booking.SessionId}/rebuild" })
        {
            var body = await ReadJsonAsync(await Client.GetAsync(route));
            Assert.DoesNotContain(reference!, StringValues(body));
        }
    }

    // ---- Cross-organizer -----------------------------------------------------

    /// <summary>
    /// The ownership boundary, checked from the direction that matters now: an
    /// authenticated non-owner must not be able to reach another organizer's
    /// event log through ANY route. Previously they could, by choosing the
    /// anonymous one - not a privilege escalation (it ignored identity entirely),
    /// but a way to sidestep OwnershipGuard by changing the URL.
    /// </summary>
    [Fact]
    public async Task AnotherOrganizerCannotReachTheEventLogThroughAnyRoute()
    {
        var booking = await ArrangeBookingAsync(email: "alice@example.com", slug: "alice-page");
        var bob = await WithDbAsync(db => TestData.AddWorkspaceAsync(db, email: "bob@example.com", slug: "bob-page"));
        using var bobClient = ClientFor(bob.Organizer);

        var removed = await bobClient.GetAsync($"/api/booking-sessions/{booking.SessionId}/timeline");
        var viaAlicesPage = await bobClient.GetAsync(
            $"/api/organizer/booking-pages/{booking.Workspace.Page.Id}/sessions/{booking.SessionId}/timeline");
        var viaHisOwnPage = await bobClient.GetAsync(
            $"/api/organizer/booking-pages/{bob.Page.Id}/sessions/{booking.SessionId}/timeline");

        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, viaAlicesPage.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, viaHisOwnPage.StatusCode);
    }

    /// <summary>
    /// And the anonymous session read grants an authenticated stranger nothing
    /// EXTRA: it is the same possession-of-session-id capability an anonymous
    /// caller has, not a wider one for being signed in. Pinned because a future
    /// "enrich the response for organizers" change here would be a genuine
    /// cross-tenant disclosure.
    /// </summary>
    [Fact]
    public async Task BeingSignedInAsAnotherOrganizerDoesNotWidenTheAnonymousSessionRead()
    {
        var booking = await ArrangeBookingAsync(email: "alice@example.com", slug: "alice-page");
        var bob = await WithDbAsync(db => TestData.AddWorkspaceAsync(db, email: "bob@example.com", slug: "bob-page"));
        using var bobClient = ClientFor(bob.Organizer);

        var anonymous = await ReadJsonAsync(await Client.GetAsync($"/api/booking-sessions/{booking.SessionId}"));
        var asBob = await ReadJsonAsync(await bobClient.GetAsync($"/api/booking-sessions/{booking.SessionId}"));

        Assert.Equal(anonymous.ToString(), asBob.ToString());
    }

    // ---- Unknown ids ---------------------------------------------------------

    /// <summary>
    /// A session id is a Guid.NewGuid() - 122 random bits, so it is not
    /// enumerable - but the responses are worth pinning anyway: both anonymous
    /// reads answer 404 for an id that names nothing, so neither leaks the
    /// existence of a session through a status code an attacker could sort on.
    /// </summary>
    [Fact]
    public async Task AnUnknownSessionIdIsNotFoundOnEveryAnonymousSurface()
    {
        var unknown = Guid.NewGuid();

        var session = await Client.GetAsync($"/api/booking-sessions/{unknown}");
        var rebuild = await Client.GetAsync($"/api/booking-sessions/{unknown}/rebuild");

        Assert.Equal(HttpStatusCode.NotFound, session.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, rebuild.StatusCode);
    }
}
