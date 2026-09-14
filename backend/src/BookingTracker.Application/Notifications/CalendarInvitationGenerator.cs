using System.Globalization;
using System.Text;
using BookingTracker.Application.Common;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Notifications.Templates;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;

namespace BookingTracker.Application.Notifications;

/// <summary>
/// See ICalendarInvitationGenerator for the contract. This class owns the two
/// decisions that make calendar updates work correctly:
///
/// UID - derived from BookingSession.Id, never stored. That id is assigned when
/// the session starts and is immutable for the life of the booking, so it
/// already satisfies RFC5545's "globally unique" requirement (a Guid) and
/// survives confirmation, resend, every reschedule, and cancellation without a
/// database column existing to be forgotten or diverge. The host suffix keeps
/// the UID qualified, per the RFC's recommendation.
///
/// SEQUENCE - derived from BookingSession.RescheduleCount. A client only applies
/// an update when the incoming SEQUENCE is higher than the one it holds, so this
/// must increase monotonically across the booking's life: confirmation is 0,
/// each reschedule is the reschedule count, and a cancellation is one higher
/// still so it always supersedes whatever the client last saw. Again no column -
/// RescheduleCount is already maintained by BookingSession.Reschedule.
/// </summary>
public class CalendarInvitationGenerator : ICalendarInvitationGenerator
{
    /// <summary>Qualifies the UID. A constant, not the request host: the UID must not change if the app is reached through a different address.</summary>
    private const string UidDomain = "bookingtracker";

    private const string FileName = "invite.ics";

    public CalendarInvitation? CreateRequest(
        BookingSession session, BookingPage page, Organizer organizer, string timeZoneId, string? viewUrl) =>
        Create(session, page, organizer, timeZoneId, viewUrl, IcsMethod.Request);

    public CalendarInvitation? CreateCancellation(
        BookingSession session, BookingPage page, Organizer organizer, string timeZoneId, string? viewUrl) =>
        Create(session, page, organizer, timeZoneId, viewUrl, IcsMethod.Cancel);

    /// <summary>The stable calendar identity of a booking. Public so tests - and any future channel - can assert it rather than re-deriving the format.</summary>
    public static string BuildUid(Guid bookingSessionId) => $"{bookingSessionId:D}@{UidDomain}";

    /// <summary>
    /// Confirmations and reschedules ride the reschedule count; a cancellation
    /// deliberately sits one above it so it always outranks the last REQUEST the
    /// client received, whatever that was.
    /// </summary>
    public static int BuildSequence(BookingSession session, IcsMethod method) =>
        method == IcsMethod.Cancel ? session.RescheduleCount + 1 : session.RescheduleCount;

    private CalendarInvitation? Create(
        BookingSession session, BookingPage page, Organizer organizer, string timeZoneId, string? viewUrl, IcsMethod method)
    {
        if (session.SelectedDate is null || session.SelectedTime is null
            || string.IsNullOrWhiteSpace(session.Email) || string.IsNullOrWhiteSpace(session.Name))
        {
            return null;
        }

        // Reuses the one conversion the rest of the system uses - a booking's stored
        // date/time is organizer-local wall clock, and the invitation needs the instant.
        var startUtc = BookingScheduleTime.ToUtc(session.SelectedDate.Value, session.SelectedTime.Value, timeZoneId);
        var endUtc = startUtc.AddMinutes(page.DurationMinutes > 0 ? page.DurationMinutes : 30);

        var context = new IcsInvitationContext(
            Uid: BuildUid(session.Id),
            Sequence: BuildSequence(session, method),
            Method: method,
            StartUtc: startUtc,
            EndUtc: endUtc,
            Summary: $"{page.Title} with {organizer.Name}",
            Description: BuildDescription(session, page, organizer, timeZoneId, viewUrl),
            // BookingPage has no location field yet; the property is omitted rather than
            // emitted empty, and starts working the moment one is added.
            Location: null,
            OrganizerName: organizer.Name,
            OrganizerEmail: organizer.Email,
            AttendeeName: session.Name!,
            AttendeeEmail: session.Email!,
            Url: viewUrl,
            // The booking's own stored meeting link - the same value the emails
            // render a Join button from. Null for an in-person booking, which
            // produces exactly the invitation this method produced before Meet
            // support existed.
            ConferenceUrl: session.MeetingUrl,
            ConferenceLabel: DescribeMeeting(session.MeetingProvider));

        return new CalendarInvitation(
            IcsCalendarInvitation.Build(context), FileName, IcsCalendarInvitation.MethodName(method));
    }

    private static string? DescribeMeeting(MeetingProviderType? provider) => provider switch
    {
        MeetingProviderType.GoogleMeet => "Google Meet",
        _ => null,
    };

    private static string BuildDescription(
        BookingSession session, BookingPage page, Organizer organizer, string timeZoneId, string? viewUrl)
    {
        // Plain text with real newlines; IcsCalendarInvitation.Escape turns them into
        // the literal \n the format requires. Building pre-escaped text here would
        // double-escape it.
        var builder = new StringBuilder();
        builder.Append(page.Title).Append(" with ").Append(organizer.Name).Append('\n');
        builder.Append("Guest: ").Append(session.Name).Append('\n');
        builder.Append("Time zone: ").Append(timeZoneId).Append('\n');
        builder.Append("Duration: ").Append(page.DurationMinutes.ToString(CultureInfo.InvariantCulture)).Append(" minutes\n");

        if (!string.IsNullOrWhiteSpace(session.BookingReference))
        {
            builder.Append("Reference: ").Append(session.BookingReference).Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(session.Message))
        {
            builder.Append("Notes: ").Append(session.Message).Append('\n');
        }

        // Also in the description, not only in the conference properties: it is
        // the one part of a VEVENT every client shows, so a link here survives
        // even where CONFERENCE, X-GOOGLE-CONFERENCE and LOCATION are all
        // ignored.
        if (!string.IsNullOrWhiteSpace(session.MeetingUrl))
        {
            builder.Append("Join ").Append(DescribeMeeting(session.MeetingProvider) ?? "meeting")
                .Append(": ").Append(session.MeetingUrl).Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(viewUrl))
        {
            builder.Append("Manage this booking: ").Append(viewUrl);
        }

        return builder.ToString().TrimEnd('\n');
    }
}
