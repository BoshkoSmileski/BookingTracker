using BookingTracker.Domain.Common;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.Domain.Exceptions;
using BookingTracker.UnitTests.TestSupport;

namespace BookingTracker.UnitTests.Domain.Entities;

/// <summary>
/// The organizer's half of custom booking questions: defining the fields on a
/// booking page. The visitor's half - answering them - is covered by
/// BookingSessionAnswerTests and, for the event-sourcing claim,
/// BookingSessionEventSourcingTests.
/// </summary>
public class BookingFormFieldTests
{
    private static BookingPage NewPage() => TestEntities.CreateBookingPage(Guid.NewGuid());

    [Fact]
    public void AddFormField_StoresLabelTypeAndRequiredness()
    {
        var page = NewPage();

        var field = page.AddFormField("Company", BookingFieldType.ShortText, isRequired: true);

        Assert.Equal("Company", field.Label);
        Assert.Equal(BookingFieldType.ShortText, field.Type);
        Assert.True(field.IsRequired);
        Assert.Equal(page.Id, field.BookingPageId);
        Assert.Single(page.FormFields);
    }

    [Fact]
    public void AddFormField_AssignsIncreasingDisplayOrder_WithoutTheCallerSupplyingOne()
    {
        var page = NewPage();

        var first = page.AddFormField("Company", BookingFieldType.ShortText, isRequired: false);
        var second = page.AddFormField("Role", BookingFieldType.ShortText, isRequired: false);
        var third = page.AddFormField("Topic", BookingFieldType.LongText, isRequired: false);

        Assert.True(first.DisplayOrder < second.DisplayOrder);
        Assert.True(second.DisplayOrder < third.DisplayOrder);
    }

    [Fact]
    public void AddFormField_AfterARemoval_StillOrdersTheNewFieldLast()
    {
        // DisplayOrder is derived from the current maximum, not the count, so a
        // gap left by a removal cannot make a new field collide with an existing
        // one - which would make the visitor's ordering non-deterministic.
        var page = NewPage();
        var first = page.AddFormField("Company", BookingFieldType.ShortText, isRequired: false);
        var second = page.AddFormField("Role", BookingFieldType.ShortText, isRequired: false);

        page.RemoveFormField(first.Id);
        var third = page.AddFormField("Topic", BookingFieldType.ShortText, isRequired: false);

        Assert.True(third.DisplayOrder > second.DisplayOrder);
    }

    [Fact]
    public void AddFormField_TrimsTheLabel()
    {
        var page = NewPage();

        var field = page.AddFormField("  Company  ", BookingFieldType.ShortText, isRequired: false);

        Assert.Equal("Company", field.Label);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddFormField_RejectsABlankLabel(string label)
    {
        var page = NewPage();

        Assert.Throws<DomainException>(() => page.AddFormField(label, BookingFieldType.ShortText, isRequired: false));
    }

    [Fact]
    public void AddFormField_RejectsALabelOverTheSharedLimit()
    {
        var page = NewPage();
        var tooLong = new string('x', BookingFieldLimits.CustomFieldLabelMaxLength + 1);

        Assert.Throws<DomainException>(() => page.AddFormField(tooLong, BookingFieldType.ShortText, isRequired: false));
    }

    [Fact]
    public void AddFormField_RefusesToExceedTheMaximum()
    {
        var page = NewPage();
        for (var i = 0; i < BookingPage.MaxFormFields; i++)
        {
            page.AddFormField($"Field {i}", BookingFieldType.ShortText, isRequired: false);
        }

        Assert.Throws<DomainException>(() => page.AddFormField("One too many", BookingFieldType.ShortText, isRequired: false));
        Assert.Equal(BookingPage.MaxFormFields, page.FormFields.Count);
    }

    [Fact]
    public void RemoveFormField_RemovesOnlyThatField()
    {
        var page = NewPage();
        var keep = page.AddFormField("Company", BookingFieldType.ShortText, isRequired: false);
        var drop = page.AddFormField("Role", BookingFieldType.ShortText, isRequired: false);

        page.RemoveFormField(drop.Id);

        Assert.Equal(keep.Id, Assert.Single(page.FormFields).Id);
    }

    [Fact]
    public void RemoveFormField_RejectsAFieldFromAnotherPage()
    {
        var page = NewPage();
        var otherPage = NewPage();
        var foreignField = otherPage.AddFormField("Company", BookingFieldType.ShortText, isRequired: false);

        Assert.Throws<DomainException>(() => page.RemoveFormField(foreignField.Id));
    }

    [Fact]
    public void FormFields_AreSeparateFromInstructions()
    {
        // The two are genuinely different features that happen to sit on the
        // same aggregate - and Domain's BookingQuestion (an instruction) has a
        // name that invites exactly this confusion, so it is pinned here.
        var page = NewPage();

        page.AddQuestion("Please bring photo ID", 0);
        page.AddFormField("Company", BookingFieldType.ShortText, isRequired: false);

        Assert.Single(page.Questions);
        Assert.Single(page.FormFields);
        Assert.Equal("Please bring photo ID", page.Questions[0].Prompt);
        Assert.Equal("Company", page.FormFields[0].Label);
    }
}
