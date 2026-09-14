using System.Net;
using BookingTracker.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.IntegrationTests;

/// <summary>
/// That one organizer cannot reach another's data, asserted through the real
/// authentication pipeline: real signed JWTs, real JwtBearer validation, real
/// [Authorize] filters, and the real OwnershipGuard inside each handler.
///
/// Ownership is enforced in handlers rather than by routing, so it is invisible
/// to a controller test and only partly visible to a handler test - which can
/// pass an organizer id directly and never proves the id came from the token.
/// That link is what these cover.
/// </summary>
public class AuthorizationBoundaryTests : ApiTestBase
{
    private sealed record TwoOrganizers(
        TestData.Workspace Alice, HttpClient AliceClient,
        TestData.Workspace Bob, HttpClient BobClient);

    private async Task<TwoOrganizers> ArrangeAsync()
    {
        var alice = await WithDbAsync(db => TestData.AddWorkspaceAsync(db, email: "alice@example.com", slug: "alice-page"));
        var bob = await WithDbAsync(db => TestData.AddWorkspaceAsync(db, email: "bob@example.com", slug: "bob-page"));
        return new TwoOrganizers(alice, ClientFor(alice.Organizer), bob, ClientFor(bob.Organizer));
    }

    // ---- An organizer's own data -------------------------------------------

    [Fact]
    public async Task AnOrganizerSeesOnlyTheirOwnBookingPages()
    {
        var s = await ArrangeAsync();

        var pages = await ReadJsonAsync(await s.AliceClient.GetAsync("/api/organizer/booking-pages"));

        Assert.Equal(1, pages.GetArrayLength());
        Assert.Equal("alice-page", pages[0].GetProperty("slug").GetString());
    }

    [Fact]
    public async Task AnOrganizerCanReadTheirOwnBookingPage()
    {
        var s = await ArrangeAsync();

        var response = await s.AliceClient.GetAsync($"/api/organizer/booking-pages/{s.Alice.Page.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AnOrganizerCanChangeTheirOwnAvailability()
    {
        var s = await ArrangeAsync();

        var response = await s.AliceClient.PutAsync("/api/organizer/availability/schedule",
            RawJson("""
            {"timeZoneId":"Europe/Skopje","days":[
              {"dayOfWeek":1,"isEnabled":true,"intervals":[{"start":"10:00:00","end":"15:00:00"}]}]}
            """));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var schedule = await WithDbAsync(db => db.WorkingSchedules.SingleAsync(w => w.OrganizerId == s.Alice.Organizer.Id));
        Assert.Equal("Europe/Skopje", schedule.TimeZoneId);
    }

    // ---- Another organizer's data ------------------------------------------

    [Fact]
    public async Task AnOrganizerCannotReadAnotherOrganizersBookingPage()
    {
        var s = await ArrangeAsync();

        var response = await s.AliceClient.GetAsync($"/api/organizer/booking-pages/{s.Bob.Page.Id}");

        // 403, from OwnershipGuard - which distinguishes "no such page" (404)
        // from "exists, not yours" (403). Pinned as the actual contract: both
        // refuse, and the split is what lets an organizer tell a mistyped id
        // from someone else's.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AnOrganizerCannotReadAnotherOrganizersSessions()
    {
        // The sessions of a booking page are the most sensitive organizer-scoped
        // data there is - every visitor's name, email and message.
        var s = await ArrangeAsync();

        var response = await s.AliceClient.GetAsync($"/api/organizer/booking-pages/{s.Bob.Page.Id}/sessions");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ANonExistentBookingPageIs404NotForbidden()
    {
        // The other half of OwnershipGuard's split, so the two cannot quietly
        // collapse into one status.
        var s = await ArrangeAsync();

        var response = await s.AliceClient.GetAsync($"/api/organizer/booking-pages/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnOrganizerCannotModifyAnotherOrganizersBookingPage()
    {
        var s = await ArrangeAsync();

        var response = await s.AliceClient.PutAsync(
            $"/api/organizer/booking-pages/{s.Bob.Page.Id}/details",
            RawJson("""{"title":"Hijacked","description":null}"""));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var bobsPage = await WithDbAsync(db => db.BookingPages.SingleAsync(p => p.Id == s.Bob.Page.Id));
        Assert.Equal("Test Meeting", bobsPage.Title);
    }

    [Fact]
    public async Task AnOrganizerCannotAddAFormFieldToAnotherOrganizersPage()
    {
        var s = await ArrangeAsync();

        var response = await s.AliceClient.PostAsync(
            $"/api/organizer/booking-pages/{s.Bob.Page.Id}/form-fields",
            RawJson("""{"label":"Injected","type":"ShortText","isRequired":false}"""));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var bobsPage = await WithDbAsync(db => db.BookingPages.SingleAsync(p => p.Id == s.Bob.Page.Id));
        Assert.Empty(bobsPage.FormFields);
    }

    [Fact]
    public async Task AnOrganizerCannotDeleteAnotherOrganizersAvailabilityException()
    {
        var s = await ArrangeAsync();
        var date = TestData.NextBookableWeekday();

        var created = await s.BobClient.PostAsync("/api/organizer/availability/exceptions",
            RawJson($$"""{"date":"{{date:yyyy-MM-dd}}","startTime":null,"endTime":null,"type":"Vacation","reason":"Bob is away"}"""));
        var exceptionId = (await ReadJsonAsync(created)).GetProperty("id").GetGuid();

        var response = await s.AliceClient.DeleteAsync($"/api/organizer/availability/exceptions/{exceptionId}");

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"Expected the delete to be refused, got {(int)response.StatusCode}.");
        Assert.Single(await WithDbAsync(db => db.AvailabilityExceptions.ToListAsync()));
    }

    [Fact]
    public async Task EachOrganizersScheduleIsSeparate()
    {
        // Availability is organizer-scoped, so Alice saving hers must leave
        // Bob's untouched - the classic cross-tenant write.
        var s = await ArrangeAsync();

        await s.AliceClient.PutAsync("/api/organizer/availability/schedule",
            RawJson("""{"timeZoneId":"Asia/Tokyo","days":[]}"""));

        var bobsSchedule = await WithDbAsync(db =>
            db.WorkingSchedules.SingleAsync(w => w.OrganizerId == s.Bob.Organizer.Id));
        Assert.Equal("UTC", bobsSchedule.TimeZoneId);
    }

    // ---- Anonymous ----------------------------------------------------------

    [Fact]
    public async Task AnAnonymousVisitorCanReadAPublicBookingPage()
    {
        await ArrangeAsync();

        var response = await Client.GetAsync("/api/booking-pages/alice-page");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/organizer/booking-pages")]
    [InlineData("/api/organizer/notification-settings")]
    [InlineData("/api/organizer/availability/schedule")]
    [InlineData("/api/organizer/availability/exceptions?from=2026-01-01&to=2026-12-31")]
    [InlineData("/api/calendar/connection")]
    [InlineData("/api/organizer/analytics/bookings")]
    public async Task AnAnonymousVisitorCannotReachOrganizerEndpoints(string path)
    {
        await ArrangeAsync();

        var response = await Client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- Token validity -----------------------------------------------------

    [Fact]
    public async Task AnExpiredTokenIsRejected()
    {
        var s = await ArrangeAsync();
        using var client = ClientWithRawToken(TestJwt.ExpiredAccessTokenFor(s.Alice.Organizer));

        var response = await client.GetAsync("/api/organizer/booking-pages");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ATokenSignedWithTheWrongKeyIsRejected()
    {
        // The signature check specifically - the thing that stops anyone minting
        // their own organizer id. A stub auth handler could never test this.
        var s = await ArrangeAsync();
        using var client = ClientWithRawToken(TestJwt.AccessTokenSignedWithTheWrongKey(s.Alice.Organizer));

        var response = await client.GetAsync("/api/organizer/booking-pages");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AGarbageBearerTokenIsRejected()
    {
        using var client = ClientWithRawToken("not-a-jwt-at-all");

        var response = await client.GetAsync("/api/organizer/booking-pages");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AValidTokenForANonExistentOrganizerLeaksNothing()
    {
        // Correctly signed, so it authenticates - but resolves to nobody. It
        // must come back empty rather than erroring or seeing everything.
        await ArrangeAsync();
        using var client = ClientWithRawToken(TestJwt.AccessTokenForUnknownOrganizer());

        var response = await client.GetAsync("/api/organizer/booking-pages");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await ReadJsonAsync(response)).GetArrayLength());
    }
}
