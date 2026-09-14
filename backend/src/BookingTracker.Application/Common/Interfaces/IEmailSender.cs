namespace BookingTracker.Application.Common.Interfaces;

/// <summary>An RFC5545 invitation to attach as text/calendar. <paramref name="Method"/> must match the METHOD inside the content - clients act on the MIME parameter.</summary>
public record EmailCalendarAttachment(string FileName, string Content, string Method);

/// <param name="Calendar">Optional invitation. Null for notifications that carry none (reminders, and anything whose invitation failed to generate).</param>
public record EmailMessage(
    string ToEmail,
    string ToName,
    string Subject,
    string HtmlBody,
    string TextBody,
    EmailCalendarAttachment? Calendar = null);

/// <summary>
/// The only email-transmission surface Application (or the email queue
/// processor, its sole caller) is allowed to know about - no SMTP, no
/// provider SDK types leak through here. Infrastructure supplies the real
/// implementation (SMTP today; swapping in or adding SendGrid/Mailgun/SES/
/// Microsoft Graph/Resend later is just a new class behind this same
/// interface, registered in DI - Application never changes).
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
