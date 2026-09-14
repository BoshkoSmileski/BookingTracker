using System.Net;
using System.Text.Json;
using BookingTracker.Domain.Enums;
using BookingTracker.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.IntegrationTests;

/// <summary>
/// Two small contracts that are only visible over HTTP.
///
/// The calendar half is deliberately narrow: no Google client is exercised and
/// no OAuth flow is driven - only the app's own API behaviour around connection
/// state and meeting settings. Building a Google test harness was explicitly out
/// of scope, and CalendarSyncServiceMeetingTests already covers the provider.
/// </summary>
public class CalendarAndMeetingContractTests : ApiTestBase
{
    private async Task<(TestData.Workspace Workspace, HttpClient Client)> ArrangeAsync(
        MeetingProviderType provider = MeetingProviderType.None)
    {
        var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(db, slug: "calendar-page", meetingProvider: provider));
        return (workspace, ClientFor(workspace.Organizer));
    }

    [Fact]
    public async Task AnOrganizerWithNoConnectionGets204RatherThanAnError()
    {
        // "No calendar connected" is a real answer, not a failure - and it
        // arrives as 204 with no body, which lib/api.ts specifically maps to
        // undefined (`if (response.status === 204) return undefined as T`).
        // Switching this to 200-with-null would change what the client receives
        // without changing anything the compiler could notice.
        var (_, client) = await ArrangeAsync();

        var response = await client.GetAsync("/api/calendar/connection");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(string.Empty, (await response.Content.ReadAsStringAsync()).Trim());
    }

    [Fact]
    public async Task ConnectionStateRequiresAuthentication()
    {
        await ArrangeAsync();

        var response = await Client.GetAsync("/api/calendar/connection");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task EachOrganizerReadsOnlyTheirOwnConnectionState()
    {
        // Scoped by the token's organizer id, never by anything the client sends.
        var (_, aliceClient) = await ArrangeAsync();
        var bob = await WithDbAsync(db => TestData.AddWorkspaceAsync(db, email: "bob@example.com", slug: "bob-calendar"));
        using var bobClient = ClientFor(bob.Organizer);

        Assert.Equal(HttpStatusCode.NoContent, (await aliceClient.GetAsync("/api/calendar/connection")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await bobClient.GetAsync("/api/calendar/connection")).StatusCode);

        Assert.Empty(await WithDbAsync(db => db.CalendarConnections.ToListAsync()));
    }

    [Fact]
    public async Task TheMeetingSettingRoundTripsAsAStringEnum()
    {
        // Same wire contract as the custom-question type, and the same failure
        // mode if it ever binds the enum directly.
        var (workspace, client) = await ArrangeAsync();

        var response = await client.PutAsync(
            $"/api/organizer/booking-pages/{workspace.Page.Id}/meeting",
            RawJson("""{"meetingProvider":"GoogleMeet"}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("GoogleMeet", body.GetProperty("meetingProvider").GetString());

        var page = await WithDbAsync(db => db.BookingPages.SingleAsync(p => p.Id == workspace.Page.Id));
        Assert.Equal(MeetingProviderType.GoogleMeet, page.MeetingProvider);
    }

    [Fact]
    public async Task AnUnknownMeetingProviderIsA400()
    {
        var (workspace, client) = await ArrangeAsync();

        var response = await client.PutAsync(
            $"/api/organizer/booking-pages/{workspace.Page.Id}/meeting",
            RawJson("""{"meetingProvider":"Zoom"}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Zoom", body);
    }

    [Fact]
    public async Task AnotherOrganizerCannotChangeTheMeetingSetting()
    {
        var (workspace, _) = await ArrangeAsync();
        var intruder = await WithDbAsync(db => TestData.AddWorkspaceAsync(db, email: "intruder@example.com", slug: "intruder-page"));
        using var intruderClient = ClientFor(intruder.Organizer);

        var response = await intruderClient.PutAsync(
            $"/api/organizer/booking-pages/{workspace.Page.Id}/meeting",
            RawJson("""{"meetingProvider":"GoogleMeet"}"""));

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"Expected the change to be refused, got {(int)response.StatusCode}.");

        var page = await WithDbAsync(db => db.BookingPages.SingleAsync(p => p.Id == workspace.Page.Id));
        Assert.Equal(MeetingProviderType.None, page.MeetingProvider);
    }

    [Fact]
    public async Task ABookingOnAMeetPageWithNoConnectionStillSucceedsWithNoMeeting()
    {
        // The fail-open contract: no calendar connection produces a booking
        // without a meeting, never a failed booking.
        await ArrangeAsync(MeetingProviderType.GoogleMeet);
        var date = TestData.NextBookableWeekday();
        var time = await BookingFlow.FirstAvailableTimeAsync(Client, "calendar-page", date);

        var confirmation = await BookingFlow.BookAsync(Client, "calendar-page", date, time);

        Assert.Equal("Submitted", confirmation.GetProperty("status").GetString());
        var session = await WithDbAsync(db => db.BookingSessions.SingleAsync());
        Assert.Null(session.MeetingUrl);
    }
}

/// <summary>
/// The rate limiter's rejection contract, exercised against the real middleware.
///
/// The two CONFIGURABLE policies are raised in the test host so a focused suite
/// does not 429 itself (every TestServer request shares one client IP). The
/// `public-token` policy is hardcoded at 20/minute in Program.cs, which makes it
/// the one that can still be exhausted here - and therefore the one that can
/// prove the shared OnRejected handler answers the way the frontend expects.
/// </summary>
public class RateLimitingContractTests : ApiTestBase
{
    [Fact]
    public async Task ExceedingThePublicTokenLimitIs429WithTheApisOwnErrorShape()
    {
        // 20 requests per minute, per client IP. The 21st must be refused in the
        // same `{ title }` shape as every other error, so a client needs no
        // special case for rate limiting.
        HttpResponseMessage? rejected = null;
        for (var i = 0; i < 40 && rejected is null; i++)
        {
            var response = await Client.GetAsync("/api/bookings/some-token-that-does-not-exist");
            if (response.StatusCode == HttpStatusCode.TooManyRequests) rejected = response;
        }

        Assert.NotNull(rejected);
        var body = await ReadJsonAsync(rejected!);
        Assert.Equal(JsonValueKind.String, body.GetProperty("title").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("title").GetString()));
    }

    [Fact]
    public async Task ARejectedRequestCarriesRetryAfter()
    {
        // Read off the limiter's own lease metadata rather than hardcoded, so a
        // client can back off correctly.
        HttpResponseMessage? rejected = null;
        for (var i = 0; i < 40 && rejected is null; i++)
        {
            var response = await Client.GetAsync("/api/bookings/another-token-that-does-not-exist");
            if (response.StatusCode == HttpStatusCode.TooManyRequests) rejected = response;
        }

        Assert.NotNull(rejected);
        Assert.True(rejected!.Headers.TryGetValues("Retry-After", out var values), "Expected a Retry-After header.");
        Assert.True(int.TryParse(values!.First(), out var seconds) && seconds > 0);
    }
}
