using BookingTracker.Domain.Entities;

namespace BookingTracker.Application.Common.Interfaces;

/// <summary>The generated invitation, ready to be persisted on the queued email and attached at send time.</summary>
/// <param name="Method">REQUEST or CANCEL - must match the METHOD inside <paramref name="Content"/>.</param>
public record CalendarInvitation(string Content, string FileName, string Method);

/// <summary>
/// Turns a booking into an RFC5545 invitation. The DI seam over
/// IcsCalendarInvitation, mirroring how IEmailTemplateRenderer sits in front of
/// EmailTemplates - and kept separate from that interface because an invitation
/// is an attachment governed by its own spec and lifecycle (UID, SEQUENCE,
/// METHOD), not an email body.
///
/// Nothing outside this abstraction decides a booking's calendar identity - see
/// the implementation for the UID and SEQUENCE strategy, which is what makes an
/// update update and a cancellation cancel rather than producing duplicates.
/// </summary>
public interface ICalendarInvitationGenerator
{
    /// <summary>
    /// A REQUEST inviting the guest, or updating an existing invitation in place
    /// when the booking has been rescheduled. Null when the booking is not in a
    /// state that can produce one.
    /// </summary>
    CalendarInvitation? CreateRequest(BookingSession session, BookingPage page, Organizer organizer, string timeZoneId, string? viewUrl);

    /// <summary>A CANCEL for the same UID at a higher SEQUENCE, so clients remove the event rather than leaving a stale one behind.</summary>
    CalendarInvitation? CreateCancellation(BookingSession session, BookingPage page, Organizer organizer, string timeZoneId, string? viewUrl);
}
