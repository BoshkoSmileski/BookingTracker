using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Notifications;
using BookingTracker.Application.Notifications.Templates;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.UnitTests.TestSupport;

namespace BookingTracker.UnitTests.Application.Notifications;

/// <summary>
/// How a booking's meeting reaches the two things a recipient actually gets:
/// the email body, and the .ics attachment their calendar consumes.
///
/// Both are pure functions of the booking, so they are exercised directly with
/// no queue, no sender and no Google - what is under test is the rendering,
/// not the plumbing that carries it.
/// </summary>
public class MeetingLinkRenderingTests
{
    private const string MeetUrl = "https://meet.google.com/abc-defg-hij";

    // ---------- Email bodies ----------

    [Theory]
    [InlineData("BookingConfirmation")]
    [InlineData("OrganizerNewBooking")]
    [InlineData("Reminder")]
    [InlineData("RescheduleConfirmation")]
    public void EmailsThatShouldCarryTheMeeting_RenderAJoinLinkInBothHtmlAndText(string template)
    {
        var content = Render(template, WithMeeting());

        Assert.Contains(MeetUrl, content.HtmlBody);
        Assert.Contains("Join Google Meet", content.HtmlBody);
        Assert.Contains($"Join Google Meet: {MeetUrl}", content.TextBody);
        // Stated as a row too, so a client that strips styling still says what
        // kind of meeting this is.
        Assert.Contains("Meeting: Google Meet", content.TextBody);
    }

    [Theory]
    [InlineData("BookingConfirmation")]
    [InlineData("OrganizerNewBooking")]
    [InlineData("Reminder")]
    [InlineData("RescheduleConfirmation")]
    [InlineData("CancellationConfirmation")]
    public void EveryEmail_ForAnInPersonBooking_OmitsTheJoinSectionEntirely(string template)
    {
        var content = Render(template, WithoutMeeting());

        // Asserted against the exact markers the meeting section emits, not
        // against the bare words: "Meeting" legitimately appears in the service
        // title ("30 Minute Meeting"), and a looser assertion would fail on
        // that rather than on anything to do with this feature.
        Assert.DoesNotContain("Join ", content.HtmlBody);
        Assert.DoesNotContain("This is an online", content.HtmlBody);
        Assert.DoesNotContain("Join ", content.TextBody);
        Assert.DoesNotContain("Meeting: ", content.TextBody);
    }

    [Fact]
    public void CancellationEmail_NeverOffersAJoinLink()
    {
        // The event is deleted at Google when a booking is cancelled, so a join
        // button would lead nowhere. The booking keeps the link as history; the
        // cancellation notice deliberately does not repeat it.
        var content = Render("CancellationConfirmation", WithMeeting());

        Assert.DoesNotContain(MeetUrl, content.HtmlBody);
        Assert.DoesNotContain(MeetUrl, content.TextBody);
    }

    // ---------- ICS attachment ----------

    [Fact]
    public void Invitation_ForAMeetingBooking_CarriesTheConferencePropertiesEveryClientNeeds()
    {
        var ics = BuildInvitation(MeetingProviderType.GoogleMeet, MeetUrl, cancelled: false)!;

        // RFC 7986 5.11 - Apple Calendar and Thunderbird.
        Assert.Contains($"CONFERENCE;VALUE=URI;FEATURE=VIDEO;LABEL=Google Meet:{MeetUrl}", Unfold(ics.Content));
        // Google Calendar's own property - what renders "Join with Google Meet".
        Assert.Contains($"X-GOOGLE-CONFERENCE:{MeetUrl}", Unfold(ics.Content));
        // Outlook reads neither and takes the link out of LOCATION.
        Assert.Contains($"LOCATION:{MeetUrl}", Unfold(ics.Content));
        // And the description, which every client shows.
        Assert.Contains($"Join Google Meet: {MeetUrl}", Unfold(ics.Content).Replace("\\n", "\n"));
    }

    [Fact]
    public void Invitation_ForAnInPersonBooking_EmitsNoConferenceProperties()
    {
        var ics = BuildInvitation(MeetingProviderType.None, meetingUrl: null, cancelled: false)!;

        Assert.DoesNotContain("CONFERENCE", ics.Content);
        Assert.DoesNotContain("X-GOOGLE-CONFERENCE", ics.Content);
        Assert.DoesNotContain("LOCATION:", ics.Content);
    }

    [Fact]
    public void CancellationInvitation_DropsTheConferenceSoNoClientKeepsAJoinButton()
    {
        var ics = BuildInvitation(MeetingProviderType.GoogleMeet, MeetUrl, cancelled: true)!;

        Assert.Contains("METHOD:CANCEL", ics.Content);
        Assert.DoesNotContain("CONFERENCE", ics.Content);
        Assert.DoesNotContain("X-GOOGLE-CONFERENCE", ics.Content);
    }

    [Fact]
    public void Invitation_WithAMeeting_StillHasTheUnchangedIdentityRulesTheRestOfTheSystemReliesOn()
    {
        // Adding a conference must not disturb UID/SEQUENCE/METHOD - those are
        // what make a client update the same event rather than create a second.
        // The UID is compared against the booking it must be derived from,
        // rather than against another booking's invitation, which would only
        // ever prove that two different Guids differ.
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id, meetingProvider: MeetingProviderType.GoogleMeet);
        var session = BookingSessionScenarios.StartFillAndSubmit(page.Id).Session;
        session.AssignMeetingLink(MeetingProviderType.GoogleMeet, MeetUrl);

        var invitation = new CalendarInvitationGenerator()
            .CreateRequest(session, page, organizer, "UTC", "https://test.local/manage/tok")!;

        Assert.Contains("METHOD:REQUEST", invitation.Content);
        Assert.Contains("SEQUENCE:0", invitation.Content);
        Assert.Equal($"UID:{CalendarInvitationGenerator.BuildUid(session.Id)}", UidLineOf(invitation.Content));
    }

    [Fact]
    public void Invitation_UidAndSequence_AreUnaffectedByAMeetingAcrossAReschedule()
    {
        // The reschedule case specifically: same UID, higher SEQUENCE, and the
        // conference still present - which together are what make a client move
        // the existing event instead of adding a second one beside it.
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id, meetingProvider: MeetingProviderType.GoogleMeet);
        var session = BookingSessionScenarios.StartFillAndSubmit(page.Id).Session;
        session.AssignMeetingLink(MeetingProviderType.GoogleMeet, MeetUrl);
        var generator = new CalendarInvitationGenerator();
        var first = generator.CreateRequest(session, page, organizer, "UTC", null)!;

        session.Reschedule(new DateOnly(2026, 9, 1), new TimeOnly(11, 0), BookingSessionScenarios.SampleContext);
        var second = generator.CreateRequest(session, page, organizer, "UTC", null)!;

        Assert.Equal(UidLineOf(first.Content), UidLineOf(second.Content));
        Assert.Contains("SEQUENCE:0", first.Content);
        Assert.Contains("SEQUENCE:1", second.Content);
        Assert.Contains($"X-GOOGLE-CONFERENCE:{MeetUrl}", Unfold(second.Content));
    }

    [Fact]
    public void ConferenceLine_IsNotTextEscaped()
    {
        // CONFERENCE and X-GOOGLE-CONFERENCE are URI values. TEXT-escaping them
        // would mangle a link carrying a query string, which Zoom and Teams
        // links routinely do.
        var url = "https://example.com/j?id=1,2;x=3";
        var context = SampleContext() with { ConferenceUrl = url, ConferenceLabel = "Google Meet" };

        var ics = Unfold(IcsCalendarInvitation.Build(context));

        Assert.Contains($"X-GOOGLE-CONFERENCE:{url}", ics);
    }

    // ---------- helpers ----------

    private static EmailContent Render(string template, BookingEmailContext ctx) => template switch
    {
        "BookingConfirmation" => EmailTemplates.BookingConfirmation(ctx),
        "OrganizerNewBooking" => EmailTemplates.OrganizerNewBooking(ctx, "jane@example.com", "+1 555 0100"),
        "Reminder" => EmailTemplates.Reminder(ctx, "24 hours"),
        "RescheduleConfirmation" => EmailTemplates.RescheduleConfirmation(ctx, "Monday, August 3, 2026", "09:00", recipientIsOrganizer: false),
        "CancellationConfirmation" => EmailTemplates.CancellationConfirmation(ctx, "Something came up", recipientIsOrganizer: false),
        _ => throw new ArgumentOutOfRangeException(nameof(template), template, null),
    };

    private static BookingEmailContext WithMeeting() =>
        BaseContext() with { MeetingLabel = "Google Meet", MeetingUrl = MeetUrl };

    private static BookingEmailContext WithoutMeeting() => BaseContext();

    private static BookingEmailContext BaseContext() => new(
        RecipientName: "Jane Doe",
        OrganizerName: "Demo Organizer",
        GuestName: "Jane Doe",
        ServiceTitle: "30 Minute Meeting",
        DateLabel: "Monday, August 10, 2026",
        TimeLabel: "09:00",
        TimeZone: "UTC",
        DurationMinutes: 30,
        Location: null,
        Notes: null,
        BookingReference: "BK-12345",
        ViewUrl: "https://test.local/manage/tok",
        CancelUrl: "https://test.local/manage/tok/cancel",
        RescheduleUrl: "https://test.local/manage/tok/reschedule",
        Answers: []);

    private static IcsInvitationContext SampleContext() => new(
        Uid: $"{Guid.NewGuid():D}@bookingtracker",
        Sequence: 0,
        Method: IcsMethod.Request,
        StartUtc: new DateTime(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc),
        EndUtc: new DateTime(2026, 8, 10, 9, 30, 0, DateTimeKind.Utc),
        Summary: "30 Minute Meeting",
        Description: "A booking",
        Location: null,
        OrganizerName: "Demo Organizer",
        OrganizerEmail: "organizer@example.com",
        AttendeeName: "Jane Doe",
        AttendeeEmail: "jane@example.com",
        Url: "https://test.local/manage/tok");

    private static CalendarInvitation? BuildInvitation(MeetingProviderType provider, string? meetingUrl, bool cancelled)
    {
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id, meetingProvider: provider);
        var session = BookingSessionScenarios.StartFillAndSubmit(page.Id).Session;
        if (meetingUrl is not null) session.AssignMeetingLink(provider, meetingUrl);

        var generator = new CalendarInvitationGenerator();
        return cancelled
            ? generator.CreateCancellation(session, page, organizer, "UTC", "https://test.local/manage/tok")
            : generator.CreateRequest(session, page, organizer, "UTC", "https://test.local/manage/tok");
    }

    /// <summary>Undoes RFC5545 line folding so an assertion can be about the property, not about where the 75-octet limit happened to fall.</summary>
    private static string Unfold(string ics) => ics.Replace("\r\n ", string.Empty);

    private static string UidLineOf(string ics) =>
        Unfold(ics).Split("\r\n").Single(l => l.StartsWith("UID:", StringComparison.Ordinal));
}
