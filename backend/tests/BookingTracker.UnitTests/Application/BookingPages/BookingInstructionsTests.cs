using BookingTracker.Application.BookingPages.Commands.BookingInstructions.AddBookingInstruction;
using BookingTracker.Application.BookingPages.Commands.BookingInstructions.RemoveBookingInstruction;
using BookingTracker.Application.BookingPages.Dtos;
using BookingTracker.Application.BookingPages.Queries.GetBookingPageBySlug;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Infrastructure.Persistence;
using BookingTracker.UnitTests.TestSupport;

namespace BookingTracker.UnitTests.Application.BookingPages;

/// <summary>
/// Booking instructions: organizer-authored guidance a visitor reads before
/// booking and never answers.
///
/// The load-bearing test here is <see cref="PublicBookingPage_CarriesTheInstructions"/>.
/// Before instructions were surfaced on the public page DTO, an organizer could
/// write them and no visitor would ever see them - the feature was storage with
/// no output. Everything else can be renamed freely; that one must keep passing.
/// </summary>
public class BookingInstructionsTests
{
    private static async Task<(BookingTrackerDbContext Db, Guid OrganizerId, Guid PageId, string Slug)> SeedAsync()
    {
        var db = InMemoryDbContextFactory.Create();
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id, slug: "consultation");
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        await db.SaveChangesAsync();
        return (db, organizer.Id, page.Id, page.Slug);
    }

    private static Task<BookingPageDetailDto> AddAsync(
        BookingTrackerDbContext db, Guid organizerId, Guid pageId, string text) =>
        new AddBookingInstructionCommandHandler(db).Handle(
            new AddBookingInstructionCommand(organizerId, pageId, text), CancellationToken.None);

    [Fact]
    public async Task Adding_StoresTheTextAndReturnsItOnThePage()
    {
        var (db, organizerId, pageId, _) = await SeedAsync();
        await using var _db = db;

        var result = await AddAsync(db, organizerId, pageId, "Please have your account number ready");

        var instruction = Assert.Single(result.Instructions);
        Assert.Equal("Please have your account number ready", instruction.Text);
        Assert.Equal(0, instruction.DisplayOrder);
    }

    [Fact]
    public async Task Instructions_KeepTheOrderTheyWereAddedIn()
    {
        var (db, organizerId, pageId, _) = await SeedAsync();
        await using var _db = db;

        await AddAsync(db, organizerId, pageId, "Bring photo ID");
        await AddAsync(db, organizerId, pageId, "Arrive five minutes early");
        var result = await AddAsync(db, organizerId, pageId, "Parking is on the north side");

        // Order is the organizer's editorial choice - the list on their settings
        // screen and the notice on the booking page must agree on it.
        Assert.Equal(
            ["Bring photo ID", "Arrive five minutes early", "Parking is on the north side"],
            result.Instructions.Select(i => i.Text));
    }

    [Fact]
    public async Task PublicBookingPage_CarriesTheInstructions()
    {
        var (db, organizerId, pageId, slug) = await SeedAsync();
        await using var _db = db;
        await AddAsync(db, organizerId, pageId, "Bring photo ID");
        await AddAsync(db, organizerId, pageId, "Arrive five minutes early");

        var page = await new GetBookingPageBySlugQueryHandler(db).Handle(
            new GetBookingPageBySlugQuery(slug), CancellationToken.None);

        // REGRESSION: the public DTO carried no instructions at all, so whatever an
        // organizer wrote here was stored and never shown to anyone.
        Assert.Equal(["Bring photo ID", "Arrive five minutes early"], page.Instructions.Select(i => i.Text));
    }

    [Fact]
    public async Task PublicBookingPage_WithoutInstructions_ReturnsAnEmptyListRatherThanNull()
    {
        var (db, _, _, slug) = await SeedAsync();
        await using var _db = db;

        var page = await new GetBookingPageBySlugQueryHandler(db).Handle(
            new GetBookingPageBySlugQuery(slug), CancellationToken.None);

        // The wizard renders nothing for an empty list; a null would crash it.
        Assert.Empty(page.Instructions);
    }

    [Fact]
    public async Task Removing_TakesItOffThePage()
    {
        var (db, organizerId, pageId, _) = await SeedAsync();
        await using var _db = db;
        await AddAsync(db, organizerId, pageId, "Bring photo ID");
        var added = await AddAsync(db, organizerId, pageId, "Arrive five minutes early");
        var toRemove = added.Instructions.Single(i => i.Text == "Bring photo ID").Id;

        var result = await new RemoveBookingInstructionCommandHandler(db).Handle(
            new RemoveBookingInstructionCommand(organizerId, pageId, toRemove), CancellationToken.None);

        Assert.Equal("Arrive five minutes early", Assert.Single(result.Instructions).Text);
    }

    [Fact]
    public async Task AnotherOrganizersPage_CannotBeEdited()
    {
        var (db, _, pageId, _) = await SeedAsync();
        await using var _db = db;
        var stranger = TestEntities.CreateOrganizer(email: "stranger@example.com");
        db.Organizers.Add(stranger);
        await db.SaveChangesAsync();

        // Forbidden, not NotFound: the page exists, it just is not theirs. A
        // 404 here would be a different (and weaker) claim than OwnershipGuard makes.
        await Assert.ThrowsAsync<ForbiddenException>(() => AddAsync(db, stranger.Id, pageId, "Sneaky"));

        await Assert.ThrowsAsync<NotFoundException>(
            () => AddAsync(db, stranger.Id, Guid.NewGuid(), "Sneaky"));
    }

    [Fact]
    public void Validator_RejectsEmptyAndOverlongText()
    {
        var validator = new AddBookingInstructionCommandValidator();
        var pageId = Guid.NewGuid();
        var organizerId = Guid.NewGuid();

        Assert.False(validator.Validate(new AddBookingInstructionCommand(organizerId, pageId, "   ")).IsValid);
        Assert.False(validator.Validate(new AddBookingInstructionCommand(organizerId, pageId, new string('x', 301))).IsValid);
        Assert.True(validator.Validate(new AddBookingInstructionCommand(organizerId, pageId, "Bring photo ID")).IsValid);
    }
}
