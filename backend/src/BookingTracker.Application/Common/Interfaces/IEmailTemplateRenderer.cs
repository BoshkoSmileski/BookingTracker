using BookingTracker.Application.Notifications.Templates;

namespace BookingTracker.Application.Common.Interfaces;

/// <summary>
/// DI seam over EmailTemplates (the actual, pure string-composition logic -
/// see that class for why it stays a plain static class rather than moving
/// wholesale behind this interface). Exists so EmailNotificationService
/// depends on an abstraction, not a static class, and so a future templating
/// approach (e.g. Razor-based) could be swapped in behind the same interface
/// without EmailNotificationService changing.
/// </summary>
public interface IEmailTemplateRenderer
{
    EmailContent RenderBookingConfirmation(BookingEmailContext context);
    EmailContent RenderOrganizerNewBooking(BookingEmailContext context, string guestEmail, string? guestPhone);
    EmailContent RenderCancellationConfirmation(BookingEmailContext context, string? reason, bool recipientIsOrganizer);
    EmailContent RenderRescheduleConfirmation(BookingEmailContext context, string oldDateLabel, string oldTimeLabel, bool recipientIsOrganizer);
    EmailContent RenderReminder(BookingEmailContext context, string windowLabel);
}
