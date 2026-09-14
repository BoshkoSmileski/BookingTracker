using BookingTracker.Application.BookingPages.Commands.BookingFormFields.AddBookingFormField;
using BookingTracker.Application.BookingPages.Commands.BookingFormFields.RemoveBookingFormField;
using BookingTracker.Application.BookingPages.Queries.GetBookingPageBySlug;
using BookingTracker.Domain.Common;
using BookingTracker.Domain.Enums;
using BookingTracker.UnitTests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.UnitTests.Application.BookingPages;

/// <summary>
/// The organizer-facing CQRS slice for custom booking questions, against a real
/// (InMemory) DbContext. The two claims worth pinning are that ownership is
/// enforced the same way every other booking-page command enforces it, and that
/// the fields actually reach the *public* page DTO - a booking form the wizard
/// is never told about is the exact failure the instructions feature shipped
/// with before it was noticed.
/// </summary>
public class BookingFormFieldCommandTests
{
    [Fact]
    public async Task Add_StoresTheFieldAndReturnsItOnTheDetailDto()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        await db.SaveChangesAsync();

        var result = await new AddBookingFormFieldCommandHandler(db).Handle(
            new AddBookingFormFieldCommand(organizer.Id, page.Id, "Company", BookingFieldType.ShortText, true),
            CancellationToken.None);

        var field = Assert.Single(result.FormFields);
        Assert.Equal("Company", field.Label);
        Assert.Equal("ShortText", field.Type);
        Assert.True(field.IsRequired);

        var reloaded = await db.BookingPages.FirstAsync(p => p.Id == page.Id);
        Assert.Single(reloaded.FormFields);
    }

    [Fact]
    public async Task Add_ReturnsFieldsInDisplayOrder()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (organizerId, pageId) = await SeedPageAsync(db);
        var handler = new AddBookingFormFieldCommandHandler(db);

        await handler.Handle(new AddBookingFormFieldCommand(organizerId, pageId, "Company", BookingFieldType.ShortText, false), CancellationToken.None);
        await handler.Handle(new AddBookingFormFieldCommand(organizerId, pageId, "Role", BookingFieldType.ShortText, false), CancellationToken.None);
        var result = await handler.Handle(
            new AddBookingFormFieldCommand(organizerId, pageId, "Topic", BookingFieldType.LongText, false), CancellationToken.None);

        Assert.Equal(["Company", "Role", "Topic"], result.FormFields.Select(f => f.Label));
    }

    [Fact]
    public async Task Add_ForAPageAnotherOrganizerOwns_IsRejected()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (_, pageId) = await SeedPageAsync(db);
        var intruder = TestEntities.CreateOrganizer("intruder@example.com");
        db.Organizers.Add(intruder);
        await db.SaveChangesAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => new AddBookingFormFieldCommandHandler(db).Handle(
            new AddBookingFormFieldCommand(intruder.Id, pageId, "Company", BookingFieldType.ShortText, false),
            CancellationToken.None));
    }

    [Fact]
    public async Task Remove_DropsOnlyThatField()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (organizerId, pageId) = await SeedPageAsync(db);
        var addHandler = new AddBookingFormFieldCommandHandler(db);

        await addHandler.Handle(new AddBookingFormFieldCommand(organizerId, pageId, "Company", BookingFieldType.ShortText, false), CancellationToken.None);
        var withBoth = await addHandler.Handle(
            new AddBookingFormFieldCommand(organizerId, pageId, "Role", BookingFieldType.ShortText, false), CancellationToken.None);
        var roleId = withBoth.FormFields.Single(f => f.Label == "Role").Id;

        var result = await new RemoveBookingFormFieldCommandHandler(db).Handle(
            new RemoveBookingFormFieldCommand(organizerId, pageId, roleId), CancellationToken.None);

        Assert.Equal("Company", Assert.Single(result.FormFields).Label);
    }

    [Fact]
    public async Task Remove_ForAPageAnotherOrganizerOwns_IsRejected()
    {
        await using var db = InMemoryDbContextFactory.Create();
        var (organizerId, pageId) = await SeedPageAsync(db);
        var added = await new AddBookingFormFieldCommandHandler(db).Handle(
            new AddBookingFormFieldCommand(organizerId, pageId, "Company", BookingFieldType.ShortText, false), CancellationToken.None);
        var intruder = TestEntities.CreateOrganizer("intruder@example.com");
        db.Organizers.Add(intruder);
        await db.SaveChangesAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => new RemoveBookingFormFieldCommandHandler(db).Handle(
            new RemoveBookingFormFieldCommand(intruder.Id, pageId, added.FormFields[0].Id), CancellationToken.None));
    }

    [Fact]
    public async Task PublicBookingPage_CarriesTheFormFields()
    {
        // Without this the wizard has nothing to render, and an organizer's
        // questions would be stored but never asked - the same defect the
        // booking instructions feature shipped with.
        await using var db = InMemoryDbContextFactory.Create();
        var (organizerId, pageId) = await SeedPageAsync(db);
        await new AddBookingFormFieldCommandHandler(db).Handle(
            new AddBookingFormFieldCommand(organizerId, pageId, "Company", BookingFieldType.ShortText, true), CancellationToken.None);

        var page = await db.BookingPages.AsNoTracking().FirstAsync(p => p.Id == pageId);
        var publicDto = await new GetBookingPageBySlugQueryHandler(db).Handle(
            new GetBookingPageBySlugQuery(page.Slug), CancellationToken.None);

        var field = Assert.Single(publicDto.FormFields);
        Assert.Equal("Company", field.Label);
        Assert.True(field.IsRequired);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("Company", true)]
    public void Validator_RequiresANonBlankLabel(string label, bool expectedValid)
    {
        var result = new AddBookingFormFieldCommandValidator().Validate(
            new AddBookingFormFieldCommand(Guid.NewGuid(), Guid.NewGuid(), label, BookingFieldType.ShortText, false));

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void Validator_RejectsALabelOverTheSharedLimit()
    {
        var overLimit = new string('x', BookingFieldLimits.CustomFieldLabelMaxLength + 1);

        var result = new AddBookingFormFieldCommandValidator().Validate(
            new AddBookingFormFieldCommand(Guid.NewGuid(), Guid.NewGuid(), overLimit, BookingFieldType.ShortText, false));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(AddBookingFormFieldCommand.Label));
    }

    private static async Task<(Guid OrganizerId, Guid PageId)> SeedPageAsync(
        BookingTracker.Infrastructure.Persistence.BookingTrackerDbContext db)
    {
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id);
        db.Organizers.Add(organizer);
        db.BookingPages.Add(page);
        await db.SaveChangesAsync();
        return (organizer.Id, page.Id);
    }
}
