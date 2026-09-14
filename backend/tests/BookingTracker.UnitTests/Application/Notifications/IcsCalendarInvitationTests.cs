using System.Text;
using BookingTracker.Application.Notifications.Templates;

namespace BookingTracker.UnitTests.Application.Notifications;

/// <summary>
/// The RFC5545 formatter itself: escaping, folding, line endings, and UTC
/// stamps. Pure string composition with no I/O, so it is exercised directly -
/// no test double involved, same as EmailTemplates.
/// </summary>
public class IcsCalendarInvitationTests
{
    private static IcsInvitationContext SampleContext(
        IcsMethod method = IcsMethod.Request,
        int sequence = 0,
        string summary = "30 Minute Meeting with Demo Organizer",
        string description = "A booking",
        string? location = null,
        string? url = "https://app.test/manage/token123",
        string organizerName = "Demo Organizer",
        string attendeeName = "Jane Doe") => new(
        Uid: "11111111-2222-3333-4444-555555555555@bookingtracker",
        Sequence: sequence,
        Method: method,
        StartUtc: new DateTime(2026, 8, 20, 8, 0, 0, DateTimeKind.Utc),
        EndUtc: new DateTime(2026, 8, 20, 8, 30, 0, DateTimeKind.Utc),
        Summary: summary,
        Description: description,
        Location: location,
        OrganizerName: organizerName,
        OrganizerEmail: "organizer@example.com",
        AttendeeName: attendeeName,
        AttendeeEmail: "jane@example.com",
        Url: url);

    /// <summary>Unfolds continuation lines so a property can be asserted as one logical value, exactly as a client parses it.</summary>
    private static List<string> LogicalLines(string ics) =>
        ics.Replace("\r\n ", string.Empty).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).ToList();

    private static string PropertyValue(string ics, string name) =>
        LogicalLines(ics).First(l => l.StartsWith(name + ":", StringComparison.Ordinal))[(name.Length + 1)..];

    [Fact]
    public void Build_EmitsTheRequiredCalendarSkeleton()
    {
        var ics = IcsCalendarInvitation.Build(SampleContext());
        var lines = LogicalLines(ics);

        Assert.Equal("BEGIN:VCALENDAR", lines[0]);
        Assert.Equal("END:VCALENDAR", lines[^1]);
        Assert.Contains("BEGIN:VEVENT", lines);
        Assert.Contains("END:VEVENT", lines);
        Assert.Contains($"PRODID:{IcsCalendarInvitation.ProductId}", lines);
        Assert.Contains("VERSION:2.0", lines);
        Assert.Contains("CALSCALE:GREGORIAN", lines);
    }

    [Fact]
    public void Build_UsesCrlfLineEndings_IncludingTheFinalLine()
    {
        var ics = IcsCalendarInvitation.Build(SampleContext());

        Assert.EndsWith("END:VCALENDAR\r\n", ics);
        // Every LF must be part of a CRLF pair - a bare LF breaks strict parsers.
        for (var i = 0; i < ics.Length; i++)
        {
            if (ics[i] == '\n') Assert.True(i > 0 && ics[i - 1] == '\r', $"bare LF at index {i}");
        }
    }

    [Fact]
    public void Build_FormatsDatesAsUtcBasicFormat()
    {
        var ics = IcsCalendarInvitation.Build(SampleContext());

        Assert.Equal("20260820T080000Z", PropertyValue(ics, "DTSTART"));
        Assert.Equal("20260820T083000Z", PropertyValue(ics, "DTEND"));
        Assert.Matches(@"^\d{8}T\d{6}Z$", PropertyValue(ics, "DTSTAMP"));
    }

    [Fact]
    public void FormatUtc_ConvertsLocalTimes_RatherThanMislabellingThem()
    {
        var local = new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Local);

        Assert.Equal(local.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'"), IcsCalendarInvitation.FormatUtc(local));
    }

    [Fact]
    public void FormatUtc_TreatsUnspecifiedKindAsUtc()
    {
        // Timestamps round-tripped through SQL Server come back Unspecified; this
        // codebase only ever hands over instants it computed as UTC.
        var unspecified = new DateTime(2026, 8, 20, 8, 0, 0, DateTimeKind.Unspecified);

        Assert.Equal("20260820T080000Z", IcsCalendarInvitation.FormatUtc(unspecified));
    }

    [Fact]
    public void Build_RequestIsConfirmedAndOpaque()
    {
        var ics = IcsCalendarInvitation.Build(SampleContext(IcsMethod.Request));

        Assert.Equal("REQUEST", PropertyValue(ics, "METHOD"));
        Assert.Equal("CONFIRMED", PropertyValue(ics, "STATUS"));
        Assert.Equal("OPAQUE", PropertyValue(ics, "TRANSP"));
    }

    [Fact]
    public void Build_CancelIsCancelledAndTransparent()
    {
        // A cancelled event must stop consuming busy time on the recipient's calendar.
        var ics = IcsCalendarInvitation.Build(SampleContext(IcsMethod.Cancel, sequence: 1));

        Assert.Equal("CANCEL", PropertyValue(ics, "METHOD"));
        Assert.Equal("CANCELLED", PropertyValue(ics, "STATUS"));
        Assert.Equal("TRANSPARENT", PropertyValue(ics, "TRANSP"));
    }

    [Fact]
    public void Build_WritesUidAndSequenceVerbatim()
    {
        var ics = IcsCalendarInvitation.Build(SampleContext(sequence: 4));

        Assert.Equal("11111111-2222-3333-4444-555555555555@bookingtracker", PropertyValue(ics, "UID"));
        Assert.Equal("4", PropertyValue(ics, "SEQUENCE"));
    }

    [Fact]
    public void Build_FormatsOrganizerAsCnPlusMailto()
    {
        var ics = IcsCalendarInvitation.Build(SampleContext());
        var organizer = LogicalLines(ics).Single(l => l.StartsWith("ORGANIZER", StringComparison.Ordinal));

        Assert.Equal("ORGANIZER;CN=Demo Organizer:mailto:organizer@example.com", organizer);
    }

    [Fact]
    public void Build_FormatsAttendeeWithRsvpForARequest()
    {
        var ics = IcsCalendarInvitation.Build(SampleContext());
        var attendee = LogicalLines(ics).Single(l => l.StartsWith("ATTENDEE", StringComparison.Ordinal));

        Assert.Contains("CN=Jane Doe", attendee);
        Assert.Contains("ROLE=REQ-PARTICIPANT", attendee);
        Assert.Contains("PARTSTAT=NEEDS-ACTION", attendee);
        Assert.Contains("RSVP=TRUE", attendee);
        Assert.EndsWith(":mailto:jane@example.com", attendee);
    }

    [Fact]
    public void Build_AttendeeIsNotAskedToRsvpToACancellation()
    {
        var ics = IcsCalendarInvitation.Build(SampleContext(IcsMethod.Cancel, sequence: 1));
        var attendee = LogicalLines(ics).Single(l => l.StartsWith("ATTENDEE", StringComparison.Ordinal));

        Assert.Contains("RSVP=FALSE", attendee);
        Assert.Contains("PARTSTAT=DECLINED", attendee);
    }

    [Fact]
    public void Build_QuotesParameterValuesContainingSeparators()
    {
        var ics = IcsCalendarInvitation.Build(SampleContext(organizerName: "Doe, Jane; Ltd"));
        var organizer = LogicalLines(ics).Single(l => l.StartsWith("ORGANIZER", StringComparison.Ordinal));

        Assert.Contains("CN=\"Doe, Jane; Ltd\"", organizer);
    }

    [Fact]
    public void Build_StripsDoubleQuotesFromParameterValues()
    {
        // A double quote inside a quoted parameter cannot be escaped in RFC5545 - it
        // has to be removed or the line becomes unparseable.
        var ics = IcsCalendarInvitation.Build(SampleContext(organizerName: "Jane \"JD\" Doe, Ltd"));
        var organizer = LogicalLines(ics).Single(l => l.StartsWith("ORGANIZER", StringComparison.Ordinal));

        Assert.DoesNotContain("\"JD\"", organizer);
        Assert.Contains("CN=\"Jane JD Doe, Ltd\"", organizer);
    }

    [Fact]
    public void Build_OmitsLocationWhenAbsent()
    {
        var ics = IcsCalendarInvitation.Build(SampleContext(location: null));

        Assert.DoesNotContain(LogicalLines(ics), l => l.StartsWith("LOCATION", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_IncludesLocationWhenPresent()
    {
        var ics = IcsCalendarInvitation.Build(SampleContext(location: "Room B, Floor 2"));

        Assert.Equal("Room B\\, Floor 2", PropertyValue(ics, "LOCATION"));
    }

    [Fact]
    public void Build_IncludesUrlUnescaped()
    {
        // URL is a URI value, not TEXT - escaping it would mangle query separators.
        var ics = IcsCalendarInvitation.Build(SampleContext(url: "https://app.test/manage/abc?x=1,2"));

        Assert.Equal("https://app.test/manage/abc?x=1,2", PropertyValue(ics, "URL"));
    }

    [Fact]
    public void Build_OmitsUrlWhenAbsent()
    {
        var ics = IcsCalendarInvitation.Build(SampleContext(url: null));

        Assert.DoesNotContain(LogicalLines(ics), l => l.StartsWith("URL:", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("a;b", "a\\;b")]
    [InlineData("a,b", "a\\,b")]
    [InlineData("a\\b", "a\\\\b")]
    [InlineData("a\nb", "a\\nb")]
    [InlineData("a\r\nb", "a\\nb")]
    // Colons are NOT escaped in TEXT values - escaping them breaks strict parsers.
    [InlineData("time 10:00", "time 10:00")]
    public void Escape_AppliesRfc5545TextRules(string input, string expected)
    {
        Assert.Equal(expected, IcsCalendarInvitation.Escape(input));
    }

    [Fact]
    public void Build_EscapesSummaryAndDescription()
    {
        var ics = IcsCalendarInvitation.Build(SampleContext(
            summary: "Design; review, part 2",
            description: "Line one\nLine two; with, separators\\and a backslash"));

        Assert.Equal("Design\\; review\\, part 2", PropertyValue(ics, "SUMMARY"));
        Assert.Equal("Line one\\nLine two\\; with\\, separators\\\\and a backslash", PropertyValue(ics, "DESCRIPTION"));
    }

    [Fact]
    public void Fold_LeavesShortLinesAlone()
    {
        const string line = "SUMMARY:Short";

        Assert.Equal(line, IcsCalendarInvitation.Fold(line));
    }

    [Fact]
    public void Fold_BreaksLongLinesAt75OctetsWithALeadingSpace()
    {
        var line = "DESCRIPTION:" + new string('x', 300);

        var folded = IcsCalendarInvitation.Fold(line);

        Assert.Contains("\r\n ", folded);
        foreach (var physical in folded.Split("\r\n"))
        {
            Assert.True(Encoding.UTF8.GetByteCount(physical) <= 75, $"line exceeded 75 octets: {physical.Length}");
        }
        // Unfolding must reproduce the original exactly.
        Assert.Equal(line, folded.Replace("\r\n ", string.Empty));
    }

    [Fact]
    public void Fold_NeverSplitsAMultiByteCharacter()
    {
        // Folding is measured in octets; splitting a 2-byte character across the fold
        // would corrupt it. Cyrillic text makes the failure obvious if it regresses.
        var line = "DESCRIPTION:" + string.Concat(Enumerable.Repeat("ЖЖЖЖЖЖЖЖЖЖ", 12));

        var folded = IcsCalendarInvitation.Fold(line);
        var unfolded = folded.Replace("\r\n ", string.Empty);

        Assert.Equal(line, unfolded);
        foreach (var physical in folded.Split("\r\n"))
        {
            Assert.True(Encoding.UTF8.GetByteCount(physical) <= 75);
        }
    }

    [Fact]
    public void Build_FoldsLongDescriptionsInPlace_AndStaysUnfoldable()
    {
        var longNote = string.Concat(Enumerable.Repeat("This is a long pre-meeting note. ", 12));
        var ics = IcsCalendarInvitation.Build(SampleContext(description: longNote));

        foreach (var physical in ics.Split("\r\n"))
        {
            Assert.True(Encoding.UTF8.GetByteCount(physical) <= 75, "an unfolded physical line exceeded 75 octets");
        }
        Assert.Equal(IcsCalendarInvitation.Escape(longNote), PropertyValue(ics, "DESCRIPTION"));
    }

    [Fact]
    public void MethodName_MapsToTheWireValues()
    {
        Assert.Equal("REQUEST", IcsCalendarInvitation.MethodName(IcsMethod.Request));
        Assert.Equal("CANCEL", IcsCalendarInvitation.MethodName(IcsMethod.Cancel));
    }
}
