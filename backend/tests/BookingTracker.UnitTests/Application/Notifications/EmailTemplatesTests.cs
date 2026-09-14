using BookingTracker.Application.Notifications.Templates;

namespace BookingTracker.UnitTests.Application.Notifications;

/// <summary>
/// EmailTemplates is pure string composition (no I/O), so it's tested
/// directly rather than through IEmailTemplateRenderer - see that interface's
/// doc comment for why it's just a thin pass-through with no logic of its
/// own worth re-testing.
/// </summary>
public class EmailTemplatesTests
{
    private static BookingEmailContext SampleContext(
        string recipientName = "Jane Doe",
        IReadOnlyList<(string Label, string Value)>? answers = null) => new(
        RecipientName: recipientName,
        OrganizerName: "Alex Organizer",
        GuestName: "Jane Doe",
        ServiceTitle: "30 Minute Meeting",
        DateLabel: "Monday, August 10, 2026",
        TimeLabel: "09:00",
        TimeZone: "UTC",
        DurationMinutes: 30,
        Location: null,
        Notes: "Looking forward to it",
        BookingReference: "BK-ABC123",
        ViewUrl: "https://app.test/manage/token123",
        CancelUrl: "https://app.test/manage/token123/cancel",
        RescheduleUrl: "https://app.test/manage/token123/reschedule",
        Answers: answers ?? []);

    [Fact]
    public void BookingConfirmation_IncludesEveryRequiredFact_InBothHtmlAndText()
    {
        var content = EmailTemplates.BookingConfirmation(SampleContext());

        Assert.Contains("30 Minute Meeting", content.Subject);
        Assert.Contains("Alex Organizer", content.Subject);

        foreach (var body in new[] { content.HtmlBody, content.TextBody })
        {
            Assert.Contains("Alex Organizer", body); // organizer
            Assert.Contains("30 Minute Meeting", body); // booking title / service
            Assert.Contains("Monday, August 10, 2026", body); // date
            Assert.Contains("09:00", body); // time
            Assert.Contains("UTC", body); // timezone
            Assert.Contains("30 min", body); // duration
            Assert.Contains("Looking forward to it", body); // notes
            Assert.Contains("BK-ABC123", body); // reference
        }

        Assert.Contains("https://app.test/manage/token123\"", content.HtmlBody); // View booking button
        Assert.Contains("https://app.test/manage/token123/cancel", content.HtmlBody);
        Assert.Contains("https://app.test/manage/token123/reschedule", content.HtmlBody);
        Assert.Contains("https://app.test/manage/token123", content.TextBody);
        Assert.Contains("https://app.test/manage/token123/cancel", content.TextBody);
        Assert.Contains("https://app.test/manage/token123/reschedule", content.TextBody);
    }

    [Fact]
    public void BookingConfirmation_HtmlEncodesRecipientName_PreventingHtmlInjection()
    {
        var content = EmailTemplates.BookingConfirmation(SampleContext(recipientName: "<script>alert(1)</script>"));

        Assert.DoesNotContain("<script>alert(1)</script>", content.HtmlBody);
        Assert.Contains("&lt;script&gt;", content.HtmlBody);
    }

    [Fact]
    public void OrganizerNewBooking_IncludesGuestContactDetails()
    {
        var content = EmailTemplates.OrganizerNewBooking(SampleContext(recipientName: "Alex Organizer"), "jane@example.com", "+1 555 0100");

        Assert.Contains("jane@example.com", content.HtmlBody);
        Assert.Contains("+1 555 0100", content.HtmlBody);
        Assert.Contains("jane@example.com", content.TextBody);
    }

    [Fact]
    public void OrganizerNewBooking_WithNoPhone_RendersAPlaceholder_NotNull()
    {
        var content = EmailTemplates.OrganizerNewBooking(SampleContext(recipientName: "Alex Organizer"), "jane@example.com", null);

        Assert.DoesNotContain("NullReferenceException", content.HtmlBody);
        Assert.Contains("—", content.HtmlBody);
    }

    [Fact]
    public void CancellationConfirmation_IncludesReason_WhenProvided()
    {
        var content = EmailTemplates.CancellationConfirmation(SampleContext(), "Schedule conflict", recipientIsOrganizer: false);

        Assert.Contains("Schedule conflict", content.HtmlBody);
        Assert.Contains("Schedule conflict", content.TextBody);
    }

    [Fact]
    public void CancellationConfirmation_WithNoReason_DoesNotBlowUp()
    {
        var content = EmailTemplates.CancellationConfirmation(SampleContext(), null, recipientIsOrganizer: true);

        Assert.Contains("Cancelled", content.Subject);
    }

    [Fact]
    public void RescheduleConfirmation_ShowsBothThePreviousAndNewTime()
    {
        var content = EmailTemplates.RescheduleConfirmation(SampleContext(), "Friday, August 7, 2026", "14:00", recipientIsOrganizer: false);

        Assert.Contains("Friday, August 7, 2026", content.HtmlBody);
        Assert.Contains("14:00", content.HtmlBody);
        Assert.Contains("Monday, August 10, 2026", content.HtmlBody); // new date
    }

    [Fact]
    public void Reminder_IncludesTheWindowLabel_InSubjectAndBody()
    {
        var content = EmailTemplates.Reminder(SampleContext(), "24 hours");

        Assert.Contains("24 hours", content.Subject);
        Assert.Contains("24 hours", content.HtmlBody);
        Assert.Contains("24 hours", content.TextBody);
    }

    [Fact]
    public void Location_WhenProvided_AppearsInBothBodies_ButIsOmittedWhenNull()
    {
        var withLocation = SampleContext() with { Location = "Conference Room B" };
        var withoutLocation = SampleContext();

        var withLocationContent = EmailTemplates.BookingConfirmation(withLocation);
        var withoutLocationContent = EmailTemplates.BookingConfirmation(withoutLocation);

        Assert.Contains("Conference Room B", withLocationContent.HtmlBody);
        Assert.DoesNotContain("Location", withoutLocationContent.HtmlBody);
    }
}
