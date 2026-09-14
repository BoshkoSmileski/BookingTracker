using System.Globalization;
using System.Text;

namespace BookingTracker.Application.Notifications.Templates;

/// <summary>What a client should do with the invitation. Maps to the ICS METHOD property and the MIME `method=` parameter, which must agree or clients ignore one of them.</summary>
public enum IcsMethod
{
    /// <summary>Create the event, or update it in place when the UID already exists and SEQUENCE is higher.</summary>
    Request,

    /// <summary>Remove/mark cancelled the event with this UID.</summary>
    Cancel,
}

/// <summary>Everything one VEVENT needs. Location is optional - BookingPage has no location field today, so the property is simply omitted rather than emitted empty.</summary>
public sealed record IcsInvitationContext(
    string Uid,
    int Sequence,
    IcsMethod Method,
    DateTime StartUtc,
    DateTime EndUtc,
    string Summary,
    string Description,
    string? Location,
    string OrganizerName,
    string OrganizerEmail,
    string AttendeeName,
    string AttendeeEmail,
    string? Url,
    /// <summary>
    /// The booking's online meeting join URL, or null. Distinct from
    /// <see cref="Url"/>, which is the booking's own management page - a client
    /// that conflated the two would offer "join" for a link that opens a web
    /// form. See the conference properties in IcsCalendarInvitation.Build.
    /// </summary>
    string? ConferenceUrl = null,
    /// <summary>Human label for the conference ("Google Meet"), used as the CONFERENCE property's LABEL parameter.</summary>
    string? ConferenceLabel = null);

/// <summary>
/// Builds RFC5545 iCalendar payloads. Pure string composition with no I/O -
/// the same shape as EmailTemplates, and for the same reason: this is
/// Application-layer knowledge (a booking becomes a calendar event) that must
/// not depend on how the result is eventually transmitted.
///
/// Written against the BCL only. A calendar library would be a large dependency
/// for what is, at this scope, a well-specified text format - and the spec's
/// hard parts (CRLF, 75-octet folding, TEXT escaping, UTC stamps) are each a few
/// lines and are covered by tests.
///
/// Times are always emitted as UTC (`...Z`) rather than local times with a
/// VTIMEZONE block. That is deliberate: UTC is unambiguous, universally
/// supported, and sidesteps shipping (and keeping current) timezone definitions
/// inside every invitation. Clients render it in the viewer's own zone.
/// </summary>
public static class IcsCalendarInvitation
{
    /// <summary>Identifies the producing product, per RFC5545 3.7.3.</summary>
    public const string ProductId = "-//BookingTracker//Booking Invitation//EN";

    private const string Version = "2.0";

    /// <summary>RFC5545 3.1: lines are folded at 75 octets (not characters - the limit is on encoded bytes).</summary>
    private const int MaxOctetsPerLine = 75;

    public static string Build(IcsInvitationContext context)
    {
        var isCancel = context.Method == IcsMethod.Cancel;
        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            $"PRODID:{ProductId}",
            $"VERSION:{Version}",
            "CALSCALE:GREGORIAN",
            $"METHOD:{MethodName(context.Method)}",
            "BEGIN:VEVENT",
            $"UID:{context.Uid}",
            $"DTSTAMP:{FormatUtc(DateTime.UtcNow)}",
            $"DTSTART:{FormatUtc(context.StartUtc)}",
            $"DTEND:{FormatUtc(context.EndUtc)}",
            $"SEQUENCE:{context.Sequence}",
            // CONFIRMED/CANCELLED is what a client acts on together with METHOD.
            $"STATUS:{(isCancel ? "CANCELLED" : "CONFIRMED")}",
            // OPAQUE = the time counts as busy. A cancelled event no longer should.
            $"TRANSP:{(isCancel ? "TRANSPARENT" : "OPAQUE")}",
            $"SUMMARY:{Escape(context.Summary)}",
            $"DESCRIPTION:{Escape(context.Description)}",
            $"ORGANIZER;CN={EscapeParameter(context.OrganizerName)}:mailto:{context.OrganizerEmail}",
            // RSVP=TRUE asks the client to offer accept/decline. NEEDS-ACTION on a new
            // or updated request; a cancellation is not something to respond to.
            $"ATTENDEE;CN={EscapeParameter(context.AttendeeName)};ROLE=REQ-PARTICIPANT;PARTSTAT={(isCancel ? "DECLINED" : "NEEDS-ACTION")};RSVP={(isCancel ? "FALSE" : "TRUE")}:mailto:{context.AttendeeEmail}",
        };

        // An online meeting IS this event's location, so it takes the LOCATION
        // slot when the booking has no physical one. That is what makes the
        // join link visible in clients that render nothing else - and it is
        // exactly what Google's own invitations do.
        var location = string.IsNullOrWhiteSpace(context.Location) && !string.IsNullOrWhiteSpace(context.ConferenceUrl)
            ? context.ConferenceUrl
            : context.Location;

        if (!string.IsNullOrWhiteSpace(location))
        {
            lines.Add($"LOCATION:{Escape(location)}");
        }

        if (!string.IsNullOrWhiteSpace(context.Url))
        {
            // URL is a URI value, not TEXT - it must not be TEXT-escaped or the
            // query-string separators would be mangled.
            lines.Add($"URL:{context.Url}");
        }

        // A cancelled event has nothing to join, so the conference properties
        // are emitted only on a REQUEST. Leaving them on a CANCEL would give a
        // client every reason to keep showing a join button for a meeting that
        // no longer exists.
        if (!isCancel && !string.IsNullOrWhiteSpace(context.ConferenceUrl))
        {
            lines.AddRange(ConferenceLines(context.ConferenceUrl, context.ConferenceLabel));
        }

        lines.Add("END:VEVENT");
        lines.Add("END:VCALENDAR");

        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            // RFC5545 3.2: every content line ends CRLF, including the last one.
            builder.Append(Fold(line)).Append("\r\n");
        }
        return builder.ToString();
    }

    /// <summary>
    /// The properties that make a calendar client show a clickable "Join
    /// meeting" affordance. Two are emitted, deliberately, because no single
    /// property is honoured everywhere:
    ///
    /// CONFERENCE (RFC 7986 5.11) is the standards-track answer, and the one
    /// Apple Calendar and Thunderbird act on. FEATURE=VIDEO says what kind of
    /// conference it is; LABEL is the text shown beside the link.
    ///
    /// X-GOOGLE-CONFERENCE is Google Calendar's own property and is what makes
    /// it render the "Join with Google Meet" button on an imported event.
    /// Non-standard, but RFC5545 3.8.8.2 explicitly reserves X- properties for
    /// exactly this, and any client that does not know it must ignore it -
    /// which is why shipping both is safe rather than a compatibility risk.
    /// Outlook is covered by neither and reads the URL out of LOCATION, which
    /// Build sets from the same value.
    ///
    /// Both are URI values, so - like URL - they must NOT be TEXT-escaped, or
    /// the query-string separators in the link would be mangled. Only the
    /// LABEL parameter is text, and it goes through parameter quoting.
    /// </summary>
    private static IEnumerable<string> ConferenceLines(string conferenceUrl, string? label)
    {
        var labelParameter = string.IsNullOrWhiteSpace(label) ? string.Empty : $";LABEL={EscapeParameter(label)}";
        yield return $"CONFERENCE;VALUE=URI;FEATURE=VIDEO{labelParameter}:{conferenceUrl}";
        yield return $"X-GOOGLE-CONFERENCE:{conferenceUrl}";
    }

    public static string MethodName(IcsMethod method) => method == IcsMethod.Cancel ? "CANCEL" : "REQUEST";

    /// <summary>RFC5545 3.3.5 UTC date-time: yyyyMMddTHHmmssZ. Any non-UTC input is converted first rather than silently mislabelled.</summary>
    public static string FormatUtc(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            // Unspecified is what SQL Server round-trips give back; the callers in this
            // codebase only ever hand over instants they computed as UTC.
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
        return utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// RFC5545 3.3.11 TEXT escaping: backslash, semicolon and comma are escaped,
    /// and newlines become the literal two-character sequence \n. Colons are NOT
    /// escaped in TEXT values - escaping them breaks clients that parse strictly.
    /// </summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': builder.Append("\\\\"); break;
                case ';': builder.Append("\\;"); break;
                case ',': builder.Append("\\,"); break;
                case '\r': break; // CRLF in source text collapses to one escaped newline
                case '\n': builder.Append("\\n"); break;
                default: builder.Append(c); break;
            }
        }
        return builder.ToString();
    }

    /// <summary>
    /// Parameter values (CN=...) follow different rules from TEXT: they are quoted
    /// when they contain a colon, semicolon or comma, and cannot contain a double
    /// quote at all - so any is stripped rather than producing an unparseable line.
    /// </summary>
    public static string EscapeParameter(string? value)
    {
        var cleaned = (value ?? string.Empty).Replace("\"", string.Empty).Replace("\r", string.Empty).Replace("\n", " ");
        return cleaned.IndexOfAny([':', ';', ',']) >= 0 ? $"\"{cleaned}\"" : cleaned;
    }

    /// <summary>
    /// RFC5545 3.1 line folding: split at 75 octets, continuing with CRLF followed
    /// by a single space. Measured in UTF-8 bytes, and never splitting a multi-byte
    /// character across the fold - a split code point would corrupt the text.
    /// </summary>
    public static string Fold(string line)
    {
        if (Encoding.UTF8.GetByteCount(line) <= MaxOctetsPerLine) return line;

        var builder = new StringBuilder(line.Length + 16);
        var octets = 0;
        var isContinuation = false;

        foreach (var rune in line.EnumerateRunes())
        {
            var runeOctets = rune.Utf8SequenceLength;
            // A continuation line spends one octet on its leading space.
            var limit = isContinuation ? MaxOctetsPerLine - 1 : MaxOctetsPerLine;

            if (octets + runeOctets > limit)
            {
                builder.Append("\r\n ");
                octets = 0;
                isContinuation = true;
            }

            builder.Append(rune);
            octets += runeOctets;
        }

        return builder.ToString();
    }
}
