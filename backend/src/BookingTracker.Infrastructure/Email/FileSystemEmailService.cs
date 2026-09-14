using System.Text;
using BookingTracker.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace BookingTracker.Infrastructure.Email;

/// <summary>
/// Dev-only IEmailSender: writes each email to disk instead of sending it, so
/// the whole notification flow (confirmation, organizer notice, cancellation,
/// reschedule, reminders) is fully exercisable and verifiable without a real
/// mail server configured. Used automatically whenever EmailSettings:UseSmtp
/// is false - see Infrastructure/DependencyInjection. Writes both the HTML
/// and plain-text bodies, since both are real, reviewable output now.
/// </summary>
public class FileSystemEmailService : IEmailSender
{
    private readonly string _outputDirectory;
    private readonly ILogger<FileSystemEmailService> _logger;

    public FileSystemEmailService(ILogger<FileSystemEmailService> logger)
    {
        _logger = logger;
        _outputDirectory = Path.Combine(Path.GetTempPath(), "BookingTracker", "sent-emails");
        Directory.CreateDirectory(_outputDirectory);
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var baseName = $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}_{Sanitize(message.ToEmail)}_{Sanitize(message.Subject)}";
        var htmlPath = Path.Combine(_outputDirectory, $"{baseName}.html");
        var textPath = Path.Combine(_outputDirectory, $"{baseName}.txt");

        await File.WriteAllTextAsync(htmlPath, message.HtmlBody, Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(textPath, message.TextBody, Encoding.UTF8, cancellationToken);

        if (message.Calendar is { } calendar)
        {
            // Written byte-for-byte as generated - no re-encoding, no newline rewriting -
            // so the file on disk is exactly what SMTP would attach and can be opened in
            // a real calendar client to verify the invitation end to end.
            var icsPath = Path.Combine(_outputDirectory, $"{baseName}.ics");
            await File.WriteAllTextAsync(icsPath, calendar.Content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
        }

        _logger.LogInformation(
            "Email not sent (SMTP disabled) - wrote '{Subject}' to {ToEmail} at {Path}{Invitation}",
            message.Subject, message.ToEmail, htmlPath,
            message.Calendar is null ? "" : $" (+ {message.Calendar.Method} invitation)");
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Where(c => !invalid.Contains(c)).ToArray());
        return cleaned.Length > 60 ? cleaned[..60] : cleaned;
    }
}
