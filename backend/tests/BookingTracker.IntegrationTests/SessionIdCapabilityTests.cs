using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BookingTracker.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.IntegrationTests;

/// <summary>
/// Earlier passes settled what a session id can be used to
/// <i>read</i>. Neither asked what it can be used to <i>write</i>, and the
/// answer is not derivable from either: <c>PublicTokenExposureTests.
/// AnAnonymousCallerWithOnlyASessionIdCannotTakeOverTheBooking</c> drives every
/// anonymous surface of an <b>already-submitted</b> booking, which is precisely
/// the state in which <c>BookingSession.EnsureActive</c> refuses every mutator.
/// It therefore establishes nothing about the window in which the guest is
/// still filling the form - which is the entire lifetime of a session id.
///
/// This suite fills that gap and is deliberately written as a <b>capability
/// matrix</b> rather than as a set of assertions about individual routes: what
/// matters is the answer to "if an attacker obtains only a session id, what can
/// they actually do?", and that answer must stay stable when routes move.
///
/// It documents the contract as measured, including the parts that are
/// permissive by design. Three of these tests assert that a write IS accepted -
/// they are not endorsements, they are the pinned scope of the capability, so
/// that a future change either keeps it or fails here and has to say why.
/// </summary>
public class SessionIdCapabilityTests : ApiTestBase
{
    private const string GuestName = "Jane Doe";
    private const string GuestEmail = "jane@example.com";

    private sealed record ActiveSession(Guid SessionId, DateOnly Date, TimeOnly Time);

    /// <summary>
    /// A session filled in to the point of being submittable, and deliberately
    /// left <b>Active</b> - the state the guest is in while typing, and the only
    /// state in which any of this is reachable.
    /// </summary>
    private async Task<ActiveSession> ArrangeActiveSessionAsync(string slug = "test-page")
    {
        await WithDbAsync(db => TestData.AddWorkspaceAsync(db, slug: slug));
        var date = TestData.NextBookableWeekday();
        var time = await BookingFlow.FirstAvailableTimeAsync(Client, slug, date);

        var sessionId = await BookingFlow.StartSessionAsync(Client, slug);
        await BookingFlow.FillAsync(Client, sessionId, date, time, GuestName, GuestEmail);

        return new ActiveSession(sessionId, date, time);
    }

    /// <summary>
    /// A second, entirely separate HTTP client. The attacker in every test below
    /// holds nothing but the session id - no cookie, no bearer token, no
    /// PublicToken - so using the guest's own client would quietly prove less
    /// than the test claims.
    /// </summary>
    private HttpClient Attacker() => Factory.CreateClient();

    // ---- READ, with only a session id ---------------------------------------

    /// <summary>
    /// Restated from the attacker's side rather than the guest's. 8I established
    /// that this endpoint returns the guest's own details by design (the resume
    /// flow); the fact worth pinning here is that "the guest's own browser" is
    /// not a thing the server can distinguish - possession of the id is the
    /// whole of the check.
    /// </summary>
    [Fact]
    public async Task AnyoneHoldingTheSessionIdCanReadTheGuestsDetails()
    {
        var session = await ArrangeActiveSessionAsync();
        using var attacker = Attacker();

        var body = await ReadJsonAsync(await attacker.GetAsync($"/api/booking-sessions/{session.SessionId}"));

        Assert.Equal(GuestName, body.GetProperty("name").GetString());
        Assert.Equal(GuestEmail, body.GetProperty("email").GetString());
    }

    // ---- WRITE, with only a session id --------------------------------------

    /// <summary>
    /// The finding this suite exists for: an <b>Active</b> session accepts
    /// appended events from anyone holding its id, and those events are applied
    /// to the projection - so a third party can overwrite the guest's name,
    /// email, message and chosen slot while the guest is still typing.
    ///
    /// This is inherent to the design rather than an oversight: the tracking
    /// endpoints are anonymous because the guest has no credential yet, and the
    /// session id is the only thing identifying the session. It is pinned so the
    /// scope is a stated contract instead of an assumption.
    /// </summary>
    [Fact]
    public async Task AnActiveSessionAcceptsAppendedEventsFromAnyoneHoldingItsId()
    {
        var session = await ArrangeActiveSessionAsync();
        using var attacker = Attacker();

        var response = await BookingFlow.AppendEventsAsync(
            attacker, session.SessionId,
            new BookingFlow.ClientEvent("FieldChanged", "Email", "attacker@example.com", 50));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = await WithDbAsync(db => db.BookingSessions
            .Where(s => s.Id == session.SessionId)
            .Select(s => s.Email)
            .SingleAsync());

        Assert.Equal("attacker@example.com", stored);
    }

    /// <summary>
    /// The beacon variant takes the same path with a different content type, so
    /// it carries the same capability. Asserted separately because it is a
    /// separate action with its own body handling - a reader checking only
    /// /events would draw the wrong conclusion about the surface as a whole.
    /// </summary>
    [Fact]
    public async Task TheBeaconEndpointCarriesTheSameWriteCapability()
    {
        var session = await ArrangeActiveSessionAsync();
        using var attacker = Attacker();

        var payload = JsonSerializer.Serialize(new[]
        {
            new { eventType = "FieldChanged", fieldName = "Message", newValue = "beacon", clientSequenceNumber = 60 },
        });

        var response = await attacker.PostAsync(
            $"/api/booking-sessions/{session.SessionId}/events/beacon",
            new StringContent(payload, Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = await WithDbAsync(db => db.BookingSessions
            .Where(s => s.Id == session.SessionId)
            .Select(s => s.Message)
            .SingleAsync());

        Assert.Equal("beacon", stored);
    }

    /// <summary>
    /// The escalation, and the reason the write capability is worth measuring at
    /// all rather than dismissing as "they can only edit their own form".
    ///
    /// Submitting is itself reachable with only a session id, and the submit
    /// response is the one surface that intentionally carries the
    /// <c>PublicToken</c> (see PublicTokenExposureTests). So a third party
    /// holding an Active session id can complete the booking and receive the
    /// credential that manages it.
    ///
    /// What bounds this is that it is not an escalation over what anonymous
    /// callers can already do: starting a session and booking a slot is the
    /// product's public flow, so the attacker could have obtained a PublicToken
    /// for a booking of their own without touching anyone else's session. What
    /// the stolen id adds is that the booking carries the <i>guest's</i> details
    /// and consumes the slot the guest was taking - an integrity and
    /// denial-of-booking problem, not a new credential path.
    /// </summary>
    [Fact]
    public async Task AnActiveSessionCanBeSubmittedByAnyoneHoldingItsIdAndTheResponseCarriesTheToken()
    {
        var session = await ArrangeActiveSessionAsync();
        using var attacker = Attacker();

        var response = await BookingFlow.SubmitAsync(attacker, session.SessionId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var token = (await ReadJsonAsync(response)).GetProperty("publicToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        // And it is a working credential, so the capability is stated as what it
        // achieves rather than as a field being present in a payload.
        var managed = await attacker.GetAsync($"/api/bookings/{token}");
        Assert.Equal(HttpStatusCode.OK, managed.StatusCode);
    }

    // ---- The window closes at submit ----------------------------------------

    /// <summary>
    /// <c>EnsureActive</c> is what bounds every write above to the Active window.
    /// Once the booking exists, the session id stops being a write capability
    /// entirely - which is why the 8H suite, driving a submitted booking, could
    /// not have found any of this.
    /// </summary>
    [Fact]
    public async Task ASubmittedSessionRejectsFurtherEvents()
    {
        var session = await ArrangeActiveSessionAsync();
        (await BookingFlow.SubmitAsync(Client, session.SessionId)).EnsureSuccessStatusCode();

        using var attacker = Attacker();
        var response = await BookingFlow.AppendEventsAsync(
            attacker, session.SessionId,
            new BookingFlow.ClientEvent("FieldChanged", "Email", "attacker@example.com", 50));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var stored = await WithDbAsync(db => db.BookingSessions
            .Where(s => s.Id == session.SessionId)
            .Select(s => s.Email)
            .SingleAsync());

        Assert.Equal(GuestEmail, stored);
    }

    /// <summary>One session is one booking: the same id cannot be spent twice.</summary>
    [Fact]
    public async Task ASubmittedSessionCannotBeSubmittedAgain()
    {
        var session = await ArrangeActiveSessionAsync();
        (await BookingFlow.SubmitAsync(Client, session.SessionId)).EnsureSuccessStatusCode();

        using var attacker = Attacker();
        Assert.Equal(HttpStatusCode.BadRequest, (await BookingFlow.SubmitAsync(attacker, session.SessionId)).StatusCode);
    }

    // ---- What the session id is NOT ------------------------------------------

    /// <summary>
    /// The boundary between the two anonymous credentials in this system. The
    /// session id governs a session; the PublicToken governs a booking. Cancel
    /// and reschedule live on the booking, so a session id must not reach them -
    /// asserted against the real routes rather than inferred from the fact that
    /// they take a different parameter name.
    /// </summary>
    [Fact]
    public async Task ASessionIdIsNotAcceptedAsABookingCredential()
    {
        var session = await ArrangeActiveSessionAsync();
        (await BookingFlow.SubmitAsync(Client, session.SessionId)).EnsureSuccessStatusCode();

        using var attacker = Attacker();
        var id = session.SessionId.ToString();

        Assert.Equal(HttpStatusCode.NotFound, (await attacker.GetAsync($"/api/bookings/{id}")).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await attacker.PostAsJsonAsync($"/api/bookings/{id}/cancel", new { reason = "not mine" })).StatusCode);

        // The body has to be a VALID one, or a model-binding 400 would stand in
        // for the credential rejection this test is about and prove nothing.
        // TimeOnly's JSON converter wants HH:mm:ss - the same shape
        // BookingManagementEndpointTests sends on the happy path.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await attacker.PostAsync(
                $"/api/bookings/{id}/reschedule",
                RawJson($$"""{"newDate":"{{session.Date:yyyy-MM-dd}}","newTime":"{{session.Time:HH:mm:ss}}"}"""))).StatusCode);
    }

    /// <summary>
    /// A session id belongs to one session and reaches no other. Trivially true
    /// today because the lookup is by primary key, and worth pinning because
    /// every claim above is scoped by "this session" - if one id could ever
    /// address two, the capability would be wider than the matrix says.
    /// </summary>
    [Fact]
    public async Task ASessionIdReachesOnlyItsOwnSession()
    {
        var first = await ArrangeActiveSessionAsync(slug: "page-one");
        await WithDbAsync(db => TestData.AddWorkspaceAsync(db, email: "second@example.com", slug: "page-two"));
        var second = await BookingFlow.StartSessionAsync(Client, "page-two");

        using var attacker = Attacker();
        var body = await ReadJsonAsync(await attacker.GetAsync($"/api/booking-sessions/{first.SessionId}"));

        Assert.Equal(first.SessionId, body.GetProperty("id").GetGuid());
        Assert.NotEqual(second, body.GetProperty("id").GetGuid());
    }
}
