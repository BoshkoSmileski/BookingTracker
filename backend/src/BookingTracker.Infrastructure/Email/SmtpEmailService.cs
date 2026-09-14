using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using BookingTracker.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BookingTracker.Infrastructure.Email;

/// <summary>
/// The "real" provider - plain SMTP via the BCL client. Swapping to SendGrid,
/// Mailgun, SES, Microsoft Graph, or Resend later means adding another
/// IEmailSender implementation next to this one and changing a single DI
/// registration; nothing in Application or any other Infrastructure class
/// needs to change. Sends as a proper multipart/alternative message (plain
/// text + HTML) so clients that can't or won't render HTML still get a
/// readable email.
///
/// Deliberately does not retry internally - a single failed attempt here
/// just throws, and EmailQueueProcessor (the only caller) is what decides
/// whether/when to retry, so retry policy lives in exactly one place.
/// </summary>
public class SmtpEmailService : IEmailSender
{
    private readonly EmailSettings _settings;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IOptions<EmailSettings> settings, ILogger<SmtpEmailService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        using var mailMessage = new MailMessage
        {
            From = new MailAddress(_settings.FromEmail, _settings.FromName),
            Subject = message.Subject,
        };
        mailMessage.To.Add(new MailAddress(message.ToEmail, message.ToName));
        mailMessage.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.TextBody, null, "text/plain"));
        mailMessage.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.HtmlBody, null, "text/html"));

        // Calendar invitations are attached two ways on purpose, because clients
        // disagree about which one they honour:
        //  - an inline text/calendar alternate view carrying `method=`, which is what
        //    makes Outlook and Apple Mail render accept/decline buttons instead of an
        //    inert file, and what tells a client to update or remove an event; and
        //  - a real .ics attachment, which is what clients that ignore the alternate
        //    view (and most webmail) let the user open or save.
        // Sending only one of the two is the usual reason invitations "work in Gmail
        // but not Outlook", or vice versa.
        if (message.Calendar is { } calendar)
        {
            var contentType = new ContentType("text/calendar")
            {
                CharSet = "utf-8",
                Parameters = { ["method"] = calendar.Method },
            };

            var view = AlternateView.CreateAlternateViewFromString(calendar.Content, contentType);
            view.TransferEncoding = TransferEncoding.Base64;
            mailMessage.AlternateViews.Add(view);

            var attachment = new Attachment(
                new MemoryStream(Encoding.UTF8.GetBytes(calendar.Content)),
                calendar.FileName,
                "text/calendar");
            attachment.ContentType.Parameters["method"] = calendar.Method;
            attachment.ContentType.CharSet = "utf-8";
            attachment.TransferEncoding = TransferEncoding.Base64;
            mailMessage.Attachments.Add(attachment);
        }

        using var client = new SmtpClient(_settings.Host, _settings.Port)
        {
            EnableSsl = _settings.EnableSsl,
        };
        if (!string.IsNullOrEmpty(_settings.Username))
        {
            client.Credentials = new NetworkCredential(_settings.Username, _settings.Password);
        }

        await client.SendMailAsync(mailMessage, cancellationToken);
        _logger.LogInformation(
            "Sent email '{Subject}' to {ToEmail} via SMTP{Invitation}.",
            message.Subject, message.ToEmail,
            message.Calendar is null ? "" : $" with a {message.Calendar.Method} calendar invitation");
    }
}
