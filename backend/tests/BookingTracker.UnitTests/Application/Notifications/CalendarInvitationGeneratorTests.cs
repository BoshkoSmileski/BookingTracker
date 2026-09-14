using BookingTracker.Application.Notifications;
using BookingTracker.Application.Notifications.Templates;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.UnitTests.TestSupport;

namespace BookingTracker.UnitTests.Application.Notifications;

/// <summary>
/// The identity rules that make calendar updates work: one stable UID for the
/// life of a booking, and a SEQUENCE that only ever increases. Both are derived
/// from state the booking already has (Id, RescheduleCount) rather than stored,
/// so these tests drive a real BookingSession through its real lifecycle
/// methods instead of hand-setting fields.
/// </summary>
public class CalendarInvitationGeneratorTests
{
    private const string TimeZoneId = "UTC";
    private const string ViewUrl = "https://app.test/manage/token123";

    private static readonly CalendarInvitationGenerator Generator = new();

    private static (BookingSession Session, BookingPage Page, Organizer Organizer) Seed(int durationMinutes = 30)
    {
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id, durationMinutes: durationMinutes);
        var result = BookingSessionScenarios.StartFillAndSubmit(page.Id, year: 2026, month: 8, day: 20, hour: 9, minute: 0);
        return (result.Session, page, organizer);
    }

    private static string Value(string ics, string name) =>
        ics.Replace("\r\n ", string.Empty)
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .First(l => l.StartsWith(name + ":", StringComparison.Ordinal))[(name.Length + 1)..];

    [Fact]
    public void CreateRequest_ProducesAConfirmedRequest()
    {
        var (session, page, organizer) = Seed();

        var invitation = Generator.CreateRequest(session, page, organizer, TimeZoneId, ViewUrl);

        Assert.NotNull(invitation);
        Assert.Equal("REQUEST", invitation!.Method);
        Assert.Equal("invite.ics", invitation.FileName);
        Assert.Equal("REQUEST", Value(invitation.Content, "METHOD"));
        Assert.Equal("CONFIRMED", Value(invitation.Content, "STATUS"));
    }

    [Fact]
    public void CreateCancellation_ProducesACancel()
    {
        var (session, page, organizer) = Seed();
        session.Cancel(CancelledByType.Customer, "changed my mind", BookingSessionScenarios.SampleContext);

        var invitation = Generator.CreateCancellation(session, page, organizer, TimeZoneId, ViewUrl);

        Assert.Equal("CANCEL", invitation!.Method);
        Assert.Equal("CANCEL", Value(invitation.Content, "METHOD"));
        Assert.Equal("CANCELLED", Value(invitation.Content, "STATUS"));
    }

    [Fact]
    public void Uid_IsDerivedFromTheImmutableSessionId()
    {
        var (session, page, organizer) = Seed();

        var invitation = Generator.CreateRequest(session, page, organizer, TimeZoneId, ViewUrl);

        Assert.Equal(CalendarInvitationGenerator.BuildUid(session.Id), Value(invitation!.Content, "UID"));
        Assert.Contains(session.Id.ToString("D"), Value(invitation.Content, "UID"));
    }

    [Fact]
    public void Uid_IsIdenticalAcrossConfirmationRescheduleAndCancellation()
    {
        // The whole point: one calendar event, updated and finally removed - never a
        // second event appearing because the identity drifted.
        var (session, page, organizer) = Seed();

        var confirmed = Generator.CreateRequest(session, page, organizer, TimeZoneId, ViewUrl)!;

        session.Reschedule(new DateOnly(2026, 8, 25), new TimeOnly(14, 0), BookingSessionScenarios.SampleContext);
        var rescheduled = Generator.CreateRequest(session, page, organizer, TimeZoneId, ViewUrl)!;

        session.Reschedule(new DateOnly(2026, 8, 27), new TimeOnly(16, 0), BookingSessionScenarios.SampleContext);
        var rescheduledAgain = Generator.CreateRequest(session, page, organizer, TimeZoneId, ViewUrl)!;

        session.Cancel(CancelledByType.Organizer, null, BookingSessionScenarios.SampleContext);
        var cancelled = Generator.CreateCancellation(session, page, organizer, TimeZoneId, ViewUrl)!;

        var uids = new[] { confirmed, rescheduled, rescheduledAgain, cancelled }.Select(i => Value(i.Content, "UID")).ToList();

        Assert.Single(uids.Distinct());
    }

    [Fact]
    public void Sequence_StartsAtZeroAndFollowsTheRescheduleCount()
    {
        var (session, page, organizer) = Seed();
        Assert.Equal("0", Value(Generator.CreateRequest(session, page, organizer, TimeZoneId, ViewUrl)!.Content, "SEQUENCE"));

        session.Reschedule(new DateOnly(2026, 8, 25), new TimeOnly(14, 0), BookingSessionScenarios.SampleContext);
        Assert.Equal("1", Value(Generator.CreateRequest(session, page, organizer, TimeZoneId, ViewUrl)!.Content, "SEQUENCE"));

        session.Reschedule(new DateOnly(2026, 8, 27), new TimeOnly(16, 0), BookingSessionScenarios.SampleContext);
        Assert.Equal("2", Value(Generator.CreateRequest(session, page, organizer, TimeZoneId, ViewUrl)!.Content, "SEQUENCE"));
    }

    [Fact]
    public void Sequence_ForACancellationOutranksTheLastRequest()
    {
        // A client only applies an update whose SEQUENCE is higher than the one it
        // holds, so a cancellation must always exceed the last REQUEST sent.
        var (session, page, organizer) = Seed();
        session.Reschedule(new DateOnly(2026, 8, 25), new TimeOnly(14, 0), BookingSessionScenarios.SampleContext);
        var lastRequest = int.Parse(Value(Generator.CreateRequest(session, page, organizer, TimeZoneId, ViewUrl)!.Content, "SEQUENCE"));

        session.Cancel(CancelledByType.Customer, null, BookingSessionScenarios.SampleContext);
        var cancelSequence = int.Parse(Value(Generator.CreateCancellation(session, page, organizer, TimeZoneId, ViewUrl)!.Content, "SEQUENCE"));

        Assert.True(cancelSequence > lastRequest, $"cancel sequence {cancelSequence} must exceed {lastRequest}");
    }

    [Fact]
    public void Sequence_NeverDecreasesAcrossTheWholeLifecycle()
    {
        var (session, page, organizer) = Seed();
        var sequences = new List<int>
        {
            int.Parse(Value(Generator.CreateRequest(session, page, organizer, TimeZoneId, ViewUrl)!.Content, "SEQUENCE")),
        };

        for (var i = 0; i < 3; i++)
        {
            session.Reschedule(new DateOnly(2026, 9, 1 + i), new TimeOnly(10, 0), BookingSessionScenarios.SampleContext);
            sequences.Add(int.Parse(Value(Generator.CreateRequest(session, page, organizer, TimeZoneId, ViewUrl)!.Content, "SEQUENCE")));
        }

        session.Cancel(CancelledByType.Customer, null, BookingSessionScenarios.SampleContext);
        sequences.Add(int.Parse(Value(Generator.CreateCancellation(session, page, organizer, TimeZoneId, ViewUrl)!.Content, "SEQUENCE")));

        Assert.Equal(sequences.OrderBy(x => x), sequences);
        Assert.Equal(sequences.Distinct().Count(), sequences.Count);
    }

    [Fact]
    public void Reschedule_MovesTheEventTimesButKeepsTheIdentity()
    {
        var (session, page, organizer) = Seed();
        var before = Generator.CreateRequest(session, page, organizer, TimeZoneId, ViewUrl)!;

        session.Reschedule(new DateOnly(2026, 8, 25), new TimeOnly(14, 0), BookingSessionScenarios.SampleContext);
        var after = Generator.CreateRequest(session, page, organizer, TimeZoneId, ViewUrl)!;

        Assert.Equal(Value(before.Content, "UID"), Value(after.Content, "UID"));
        Assert.NotEqual(Value(before.Content, "DTSTART"), Value(after.Content, "DTSTART"));
        Assert.Equal("20260825T140000Z", Value(after.Content, "DTSTART"));
        Assert.Equal("20260825T143000Z", Value(after.Content, "DTEND"));
    }

    [Fact]
    public void EventEnd_UsesTheBookingPageDuration()
    {
        var (session, page, organizer) = Seed(durationMinutes: 45);

        var invitation = Generator.CreateRequest(session, page, organizer, TimeZoneId, ViewUrl)!;

        Assert.Equal("20260820T090000Z", Value(invitation.Content, "DTSTART"));
        Assert.Equal("20260820T094500Z", Value(invitation.Content, "DTEND"));
    }

    [Fact]
    public void Times_AreConvertedFromTheOrganizersZoneToUtc()
    {
        // The booking stores organizer-local wall clock; the invitation must carry the
        // instant. Europe/Skopje is UTC+2 in August.
        var (session, page, organizer) = Seed();

        var invitation = Generator.CreateRequest(session, page, organizer, "Europe/Skopje", ViewUrl)!;

        Assert.Equal("20260820T070000Z", Value(invitation.Content, "DTSTART"));
    }

    [Fact]
    public void UnknownTimeZone_FallsBackToUtcRatherThanFailing()
    {
        var (session, page, organizer) = Seed();

        var invitation = Generator.CreateRequest(session, page, organizer, "Not/AZone", ViewUrl);

        Assert.NotNull(invitation);
        Assert.Equal("20260820T090000Z", Value(invitation!.Content, "DTSTART"));
    }

    [Fact]
    public void Summary_NamesTheServiceAndOrganizer()
    {
        var (session, page, organizer) = Seed();

        var invitation = Generator.CreateRequest(session, page, organizer, TimeZoneId, ViewUrl)!;

        Assert.Equal($"{page.Title} with {organizer.Name}", Value(invitation.Content, "SUMMARY"));
    }

    [Fact]
    public void Description_CarriesTheBookingDetails_AndIsEscaped()
    {
        var (session, page, organizer) = Seed();

        var description = Value(Generator.CreateRequest(session, page, organizer, TimeZoneId, ViewUrl)!.Content, "DESCRIPTION");

        Assert.Contains(session.Name!, description);
        Assert.Contains(session.BookingReference!, description);
        Assert.Contains(ViewUrl, description);
        // Multi-line description must arrive as escaped \n, never a raw newline.
        Assert.Contains("\\n", description);
    }

    [Fact]
    public void AttendeeAndOrganizer_UseTheRealAddresses()
    {
        var (session, page, organizer) = Seed();

        // Unfolded first: the ATTENDEE line exceeds 75 octets and is legitimately
        // folded, so the address only appears contiguously once continuation lines
        // are joined - exactly what a client does before reading the value.
        var content = Generator.CreateRequest(session, page, organizer, TimeZoneId, ViewUrl)!.Content
            .Replace("\r\n ", string.Empty);

        Assert.Contains($"mailto:{organizer.Email}", content);
        Assert.Contains($"mailto:{session.Email}", content);
    }

    [Fact]
    public void ReturnsNull_WhenTheBookingHasNoSlotOrGuest()
    {
        // An in-progress session has nothing to invite anyone to; callers queue the
        // email without an attachment rather than failing.
        var organizer = TestEntities.CreateOrganizer();
        var page = TestEntities.CreateBookingPage(organizer.Id);
        var (session, _) = BookingSession.Start(page.Id, BookingSessionScenarios.SampleContext);

        Assert.Null(Generator.CreateRequest(session, page, organizer, TimeZoneId, ViewUrl));
        Assert.Null(Generator.CreateCancellation(session, page, organizer, TimeZoneId, ViewUrl));
    }

    [Fact]
    public void BuildUid_IsStableForTheSameSessionAndUniquePerBooking()
    {
        var id = Guid.NewGuid();

        Assert.Equal(CalendarInvitationGenerator.BuildUid(id), CalendarInvitationGenerator.BuildUid(id));
        Assert.NotEqual(CalendarInvitationGenerator.BuildUid(id), CalendarInvitationGenerator.BuildUid(Guid.NewGuid()));
        Assert.Contains("@", CalendarInvitationGenerator.BuildUid(id));
    }

    [Fact]
    public void BuildSequence_IsPureAndMatchesTheDocumentedRule()
    {
        var (session, _, _) = Seed();

        Assert.Equal(0, CalendarInvitationGenerator.BuildSequence(session, IcsMethod.Request));
        Assert.Equal(1, CalendarInvitationGenerator.BuildSequence(session, IcsMethod.Cancel));

        session.Reschedule(new DateOnly(2026, 8, 25), new TimeOnly(14, 0), BookingSessionScenarios.SampleContext);

        Assert.Equal(1, CalendarInvitationGenerator.BuildSequence(session, IcsMethod.Request));
        Assert.Equal(2, CalendarInvitationGenerator.BuildSequence(session, IcsMethod.Cancel));
    }
}
