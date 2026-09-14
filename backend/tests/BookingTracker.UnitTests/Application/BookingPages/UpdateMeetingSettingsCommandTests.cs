using BookingTracker.Application.BookingPages.Commands.UpdateMeetingSettings;
using BookingTracker.Application.BookingPages.Queries.GetBookingPageBySlug;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Domain.Enums;
using BookingTracker.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.UnitTests.Application.BookingPages;

/// <summary>
/// The organizer's meeting-type setting, against a real (InMemory) DbContext.
/// Same two claims as every other booking-page command: ownership is enforced
/// the way the shared guard enforces it, and the setting reaches the *public*
/// page DTO - which is what the wizard needs in order to say "online meeting"
/// before a visitor commits to a slot.
/// </summary>
public class UpdateMeetingSettingsCommandTests
{
    [Fact]
    public async Task DefaultsToInPerson()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (organizer, page) = await SeedAsync(db);

        var reloaded = await db.BookingPages.FirstAsync(p => p.Id == page.Id);
        Assert.Equal(MeetingProviderType.None, reloaded.MeetingProvider);
        Assert.Equal(organizer.Id, reloaded.OrganizerId);
    }

    [Fact]
    public async Task StoresTheChoiceAndReturnsItOnTheDetailDto()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (organizer, page) = await SeedAsync(db);

        var result = await new UpdateMeetingSettingsCommandHandler(db).Handle(
            new UpdateMeetingSettingsCommand(organizer.Id, page.Id, MeetingProviderType.GoogleMeet),
            CancellationToken.None);

        Assert.Equal("GoogleMeet", result.MeetingProvider);
        Assert.Equal(MeetingProviderType.GoogleMeet, (await db.BookingPages.FirstAsync(p => p.Id == page.Id)).MeetingProvider);
    }

    [Fact]
    public async Task CanBeSwitchedBackToInPerson()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (organizer, page) = await SeedAsync(db);
        var handler = new UpdateMeetingSettingsCommandHandler(db);

        await handler.Handle(new UpdateMeetingSettingsCommand(organizer.Id, page.Id, MeetingProviderType.GoogleMeet), CancellationToken.None);
        var result = await handler.Handle(
            new UpdateMeetingSettingsCommand(organizer.Id, page.Id, MeetingProviderType.None), CancellationToken.None);

        Assert.Equal("None", result.MeetingProvider);
    }

    [Fact]
    public async Task ReachesThePublicBookingPageDto()
    {
        // The failure mode the booking-instructions feature actually shipped
        // with: an organizer setting that is stored but never told to the
        // wizard. Pinned here so it cannot regress to storage-only.
        await using var db = InMemoryDbContextFactory.Create();
        var (organizer, page) = await SeedAsync(db);
        await new UpdateMeetingSettingsCommandHandler(db).Handle(
            new UpdateMeetingSettingsCommand(organizer.Id, page.Id, MeetingProviderType.GoogleMeet),
            CancellationToken.None);

        var publicPage = await new GetBookingPageBySlugQueryHandler(db).Handle(
            new GetBookingPageBySlugQuery(page.Slug), CancellationToken.None);

        Assert.Equal("GoogleMeet", publicPage.MeetingProvider);
    }

    [Fact]
    public async Task AnotherOrganizersPage_IsForbidden()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (_, page) = await SeedAsync(db);
        var intruder = TestEntities.CreateOrganizer("intruder@example.com");
        db.Organizers.Add(intruder);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => new UpdateMeetingSettingsCommandHandler(db).Handle(
            new UpdateMeetingSettingsCommand(intruder.Id, page.Id, MeetingProviderType.GoogleMeet),
            CancellationToken.None));
    }

    [Fact]
    public async Task AnUnknownPage_IsNotFound()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (organizer, _) = await SeedAsync(db);

        await Assert.ThrowsAsync<NotFoundException>(() => new UpdateMeetingSettingsCommandHandler(db).Handle(
            new UpdateMeetingSettingsCommand(organizer.Id, Guid.NewGuid(), MeetingProviderType.GoogleMeet),
            CancellationToken.None));
    }

    [Fact]
    public async Task ChangingTheSetting_LeavesExistingBookingsMeetingsAlone()
    {
        // A meeting link already issued to a guest is never revoked by a
        // settings change - the page's setting is what future bookings get.
        await using var db = InMemoryDbContextFactory.Create();
        var (organizer, page) = await SeedAsync(db, MeetingProviderType.GoogleMeet);

        var booking = BookingSessionScenarios.StartFillAndSubmit(page.Id);
        booking.Session.AssignMeetingLink(MeetingProviderType.GoogleMeet, "https://meet.google.com/abc-defg-hij");
        db.BookingSessions.Add(booking.Session);
        await db.SaveChangesAsync();

        await new UpdateMeetingSettingsCommandHandler(db).Handle(
            new UpdateMeetingSettingsCommand(organizer.Id, page.Id, MeetingProviderType.None), CancellationToken.None);

        var reloaded = await db.BookingSessions.FirstAsync(s => s.Id == booking.Session.Id);
        Assert.Equal("https://meet.google.com/abc-defg-hij", reloaded.MeetingUrl);
        Assert.Equal(MeetingProviderType.GoogleMeet, reloaded.MeetingProvider);
    }

    // Fully qualified: this test's own namespace already has Domain and
    // Infrastructure segments, which shadow the production ones.
    private static async Task<(BookingTracker.Domain.Entities.Organizer Organizer, BookingTracker.Domain.Entities.BookingPage Page)> SeedAsync(
        BookingTracker.Infrastructure.Persistence.BookingTrackerDbContext db,
        MeetingProviderType meetingProvider = MeetingProviderType.None)
    {
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id, meetingProvider: meetingProvider);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        await db.SaveChangesAsync();
        return (organizer, page);
    }
}
