using BookingTracker.Application.Notifications.Templates;

namespace BookingTracker.UnitTests.Application.Notifications;

/// <summary>
/// What a transactional email looks like, asserted against the rendered HTML
/// rather than against the private constants that produce it - a test reading
/// <c>EmailTemplates</c>'s own fields would pass whatever those fields said.
///
/// The point is the journey: a guest goes booking page -> email -> manage page,
/// and until this shipped the middle step was the only one still wearing the
/// design the app had before it had an accent at all. Primary actions were
/// near-black (<c>#111827</c>) and the join button was a Tailwind default teal
/// (<c>#0f766e</c>) close enough to the accent to read as a mistake. Both are
/// asserted absent, because "the old button colour came back" is exactly the
/// kind of regression nothing else here would catch.
///
/// Semantic and neutral colours are deliberately NOT asserted: body text stays
/// gray-900, borders stay gray-200, and a future warning or error tone has every
/// right to be its own colour. Only the product's primary action is the accent.
/// </summary>
public class EmailBrandingTests
{
    /// <summary>accent-600, mirroring the frontend's @theme palette in index.css.</summary>
    private const string Accent = "#316e63";

    private const string RetiredPrimary = "#111827";
    private const string RetiredJoinTeal = "#0f766e";

    private const string MeetUrl = "https://meet.google.com/abc-defg-hij";
    private const string BookAgainUrl = "https://test.local/book/demo";

    public static TheoryData<string> EveryTemplate() =>
    [
        "BookingConfirmation", "OrganizerNewBooking", "Reminder", "RescheduleConfirmation", "CancellationConfirmation",
    ];

    [Theory]
    [MemberData(nameof(EveryTemplate))]
    public void EveryEmail_DrawsItsPrimaryActionInTheProductAccent(string template)
    {
        var html = Render(template, Full()).HtmlBody;

        Assert.Contains($"background:{Accent}", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(EveryTemplate))]
    public void EveryEmail_UsesNeitherRetiredButtonColour(string template)
    {
        var html = Render(template, Full()).HtmlBody;

        // Near-black as a *fill* is the retired primary button. It is still the
        // body/value text colour, which is why this asserts on the background
        // declaration rather than on the hex appearing anywhere at all.
        Assert.DoesNotContain($"background:{RetiredPrimary}", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(RetiredJoinTeal, html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(EveryTemplate))]
    public void EveryEmail_SitsOnTheSameWarmSurfaceAsTheGuestBookingScreens(string template)
    {
        // shell-100 - what PublicShell puts behind the booking sheet and the
        // manage page. The cool #f9fafb it replaced is the generic centred-card
        // background the palette note in index.css exists to avoid.
        var html = Render(template, Full()).HtmlBody;

        Assert.Contains("background:#f2f0eb", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#f9fafb", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(EveryTemplate))]
    public void EveryEmail_HasNoTextBelowTheReadableGreyTheAppSettledOn(string template)
    {
        // gray-400 measures about 2.6:1 on white. The app removed it from
        // everything readable; the email footer was still using it.
        var html = Render(template, Full()).HtmlBody;

        Assert.DoesNotContain("#9ca3af", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepaintingTheButtons_LeavesTheGoogleMeetJoinLinkExactlyWhereItWas()
    {
        // Regression guard for the one feature these styles sit on top of: the
        // join button is the loudest thing in the email precisely because it is
        // the action taken at the moment a meeting starts.
        foreach (var template in new[] { "BookingConfirmation", "OrganizerNewBooking", "Reminder", "RescheduleConfirmation" })
        {
            var content = Render(template, Full());

            Assert.Contains(MeetUrl, content.HtmlBody);
            Assert.Contains("Join Google Meet", content.HtmlBody);
            Assert.Contains($"Join Google Meet: {MeetUrl}", content.TextBody);
        }
    }

    // ---------- B7: Book another time ----------

    [Fact]
    public void CancellationEmail_ToTheGuest_OffersTheBookingPageItWasBookedOn()
    {
        var content = EmailTemplates.CancellationConfirmation(Full(), "Change of plans", recipientIsOrganizer: false);

        Assert.Contains("Book another time", content.HtmlBody);
        Assert.Contains($"href=\"{BookAgainUrl}\"", content.HtmlBody);
        Assert.Contains($"Book another time: {BookAgainUrl}", content.TextBody);
    }

    [Fact]
    public void CancellationEmail_ToTheOrganizer_DoesNotOfferToRebook()
    {
        // It is their own page. Pointing an organizer at its public booking
        // screen is the email telling them to book with themselves.
        var content = EmailTemplates.CancellationConfirmation(Full(), "Change of plans", recipientIsOrganizer: true);

        Assert.DoesNotContain("Book another time", content.HtmlBody);
        Assert.DoesNotContain("Book another time", content.TextBody);
    }

    [Fact]
    public void CancellationEmail_WithNoBookingPageUrl_SimplyOmitsTheAction()
    {
        var content = EmailTemplates.CancellationConfirmation(
            Full() with { BookAgainUrl = null }, reason: null, recipientIsOrganizer: false);

        Assert.DoesNotContain("Book another time", content.HtmlBody);
        Assert.DoesNotContain("Book another time", content.TextBody);
        // Still a complete cancellation email, not a broken one.
        Assert.Contains("Booking cancelled", content.HtmlBody);
    }

    [Theory]
    [InlineData("BookingConfirmation")]
    [InlineData("OrganizerNewBooking")]
    [InlineData("Reminder")]
    [InlineData("RescheduleConfirmation")]
    public void EmailsForABookingThatStillExists_DoNotOfferToRebook(string template)
    {
        // "Book another time" is the cancellation email's action specifically,
        // because it is the only one whose recipient has no booking left. The
        // others carry View/Reschedule/Cancel, which all still lead somewhere.
        var content = Render(template, Full());

        Assert.DoesNotContain("Book another time", content.HtmlBody);
        Assert.DoesNotContain("Book another time", content.TextBody);
    }

    // ---------- helpers ----------

    /// <summary>Everything switched on, so one context exercises every optional section a template can draw.</summary>
    private static BookingEmailContext Full() => new(
        RecipientName: "Jane Doe",
        OrganizerName: "Demo Organizer",
        GuestName: "Jane Doe",
        ServiceTitle: "30 Minute Meeting",
        DateLabel: "Monday, August 10, 2026",
        TimeLabel: "09:00",
        TimeZone: "Europe/Skopje",
        DurationMinutes: 30,
        Location: null,
        Notes: "Looking forward to it",
        BookingReference: "BK-12345",
        ViewUrl: "https://test.local/manage/tok",
        CancelUrl: "https://test.local/manage/tok/cancel",
        RescheduleUrl: "https://test.local/manage/tok/reschedule",
        Answers: [],
        MeetingLabel: "Google Meet",
        MeetingUrl: MeetUrl,
        BookAgainUrl: BookAgainUrl);

    private static EmailContent Render(string template, BookingEmailContext ctx) => template switch
    {
        "BookingConfirmation" => EmailTemplates.BookingConfirmation(ctx),
        "OrganizerNewBooking" => EmailTemplates.OrganizerNewBooking(ctx, "jane@example.com", "+1 555 0100"),
        "Reminder" => EmailTemplates.Reminder(ctx, "24 hours"),
        "RescheduleConfirmation" => EmailTemplates.RescheduleConfirmation(ctx, "Friday, August 7, 2026", "14:00", recipientIsOrganizer: false),
        "CancellationConfirmation" => EmailTemplates.CancellationConfirmation(ctx, "Change of plans", recipientIsOrganizer: false),
        _ => throw new ArgumentOutOfRangeException(nameof(template), template, null),
    };
}
