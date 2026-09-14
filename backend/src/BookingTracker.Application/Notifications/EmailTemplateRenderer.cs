using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Notifications.Templates;

namespace BookingTracker.Application.Notifications;

/// <summary>Thin DI seam over EmailTemplates - see IEmailTemplateRenderer for why this exists as an interface at all.</summary>
public class EmailTemplateRenderer : IEmailTemplateRenderer
{
    public EmailContent RenderBookingConfirmation(BookingEmailContext context) =>
        EmailTemplates.BookingConfirmation(context);

    public EmailContent RenderOrganizerNewBooking(BookingEmailContext context, string guestEmail, string? guestPhone) =>
        EmailTemplates.OrganizerNewBooking(context, guestEmail, guestPhone);

    public EmailContent RenderCancellationConfirmation(BookingEmailContext context, string? reason, bool recipientIsOrganizer) =>
        EmailTemplates.CancellationConfirmation(context, reason, recipientIsOrganizer);

    public EmailContent RenderRescheduleConfirmation(BookingEmailContext context, string oldDateLabel, string oldTimeLabel, bool recipientIsOrganizer) =>
        EmailTemplates.RescheduleConfirmation(context, oldDateLabel, oldTimeLabel, recipientIsOrganizer);

    public EmailContent RenderReminder(BookingEmailContext context, string windowLabel) =>
        EmailTemplates.Reminder(context, windowLabel);
}
