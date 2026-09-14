using System.Net;
using BookingTracker.Domain.Enums;
using BookingTracker.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookingTracker.IntegrationTests;

/// <summary>
/// Proves the test host itself is sound before anything relies on it: the real
/// app boots, requests reach real controllers, and every outside-world seam is
/// genuinely replaced. If these fail, no other integration result means anything.
/// </summary>
public class TestHostSmokeTests : ApiTestBase
{
    [Fact]
    public async Task TheRealApplicationBootsAndServesARequest()
    {
        var response = await Client.GetAsync("/api/booking-pages/does-not-exist");

        // A 404 from the app's own ExceptionHandlingMiddleware - not a connection
        // failure, and not a 500 from a half-configured host.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TheDatabaseIsInMemoryAndEmpty()
    {
        // If this ever finds rows, the host has attached to a real database or
        // DevelopmentSeeder has run - either of which makes every other test a
        // liar about what it seeded.
        await WithDbAsync(async db =>
        {
            Assert.Equal("Microsoft.EntityFrameworkCore.InMemory", db.Database.ProviderName);
            Assert.Empty(await db.Organizers.ToListAsync());
            Assert.Empty(await db.BookingPages.ToListAsync());
        });
    }

    [Fact]
    public async Task NoBackgroundSweeperIsRunning()
    {
        // The email queue processor in particular: these tests assert on queued
        // rows, which only stay observable because nothing drains them.
        var hostedServices = Factory.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .Where(s => s.GetType().Namespace?.StartsWith("BookingTracker") == true)
            .ToList();

        Assert.Empty(hostedServices);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task TheEmailSenderAndCalendarAreTestDoubles()
    {
        using var scope = Factory.Services.CreateScope();

        Assert.IsType<RecordingEmailSender>(
            scope.ServiceProvider.GetRequiredService<BookingTracker.Application.Common.Interfaces.IEmailSender>());
        Assert.IsType<RecordingCalendarSyncService>(
            scope.ServiceProvider.GetRequiredService<BookingTracker.Application.Common.Interfaces.ICalendarSyncService>());

        await Task.CompletedTask;
    }

    [Fact]
    public async Task SeedingReachesTheSameDatabaseTheApiReads()
    {
        var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(db, slug: "smoke-page"));

        var response = await Client.GetAsync("/api/booking-pages/smoke-page");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal(workspace.Page.Id.ToString(), body.GetProperty("id").GetString());
    }

    [Fact]
    public async Task AnAuthenticatedRequestResolvesTheOrganizerFromItsRealJwt()
    {
        // The token is validated by the real JwtBearer pipeline and the id is
        // read by the real ClaimsPrincipalExtensions.GetOrganizerId - neither is
        // stubbed, which is the whole reason no test auth scheme exists.
        var workspace = await WithDbAsync(db => TestData.AddWorkspaceAsync(db, slug: "auth-smoke"));
        using var client = ClientFor(workspace.Organizer);

        var response = await client.GetAsync("/api/organizer/booking-pages");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var pages = await ReadJsonAsync(response);
        Assert.Equal("auth-smoke", pages[0].GetProperty("slug").GetString());
    }

    [Fact]
    public async Task MeetingProviderIsSeededAsConfigured()
    {
        var workspace = await WithDbAsync(db =>
            TestData.AddWorkspaceAsync(db, slug: "meet-smoke", meetingProvider: MeetingProviderType.GoogleMeet));

        Assert.Equal(MeetingProviderType.GoogleMeet, workspace.Page.MeetingProvider);
    }
}
