namespace BookingTracker.Infrastructure.Email;

public class EmailSettings
{
    public const string SectionName = "Email";

    /// <summary>
    /// When false (the default for local development, since no real SMTP
    /// server is configured out of the box), emails are written to disk
    /// instead of sent - see FileSystemEmailService. Set true once a real
    /// SMTP/SendGrid/etc. provider is configured.
    /// </summary>
    public bool UseSmtp { get; set; }

    public string Host { get; set; } = default!;
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool EnableSsl { get; set; } = true;
    public string FromEmail { get; set; } = "noreply@example.com";
    public string FromName { get; set; } = "BookingTracker";

    /// <summary>
    /// Fails fast at startup when SMTP is switched on but not actually
    /// configured, rather than letting every notification discover it one at a
    /// time at send time.
    ///
    /// Without this, `UseSmtp: true` with an empty Host is close to invisible:
    /// the booking still succeeds (queueing is best-effort and sending happens
    /// out-of-band), and each notification then burns its full retry budget
    /// before being marked permanently Failed - roughly a quarter of an hour of
    /// backoff per email, with a log warning as the only symptom, by which time
    /// the emails are unrecoverable. Same reasoning and the same shape as
    /// Program.cs's Jwt:Secret check: a missing config should stop the process,
    /// not degrade quietly.
    ///
    /// Username/Password are deliberately NOT required - an unauthenticated
    /// relay (MailHog, Papercut, a local smtp4dev) is a legitimate and common
    /// development setup.
    /// </summary>
    public void EnsureValidForSending()
    {
        if (!UseSmtp) return;

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(Host)) missing.Add($"{SectionName}:Host");
        if (string.IsNullOrWhiteSpace(FromEmail)) missing.Add($"{SectionName}:FromEmail");

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"{SectionName}:UseSmtp is true but {string.Join(" and ", missing)} " +
                $"{(missing.Count == 1 ? "is" : "are")} not set. Configure the SMTP provider, or set " +
                $"{SectionName}:UseSmtp to false to write emails to disk instead " +
                "(see FileSystemEmailService). Credentials belong in User Secrets or environment " +
                "variables, never in appsettings.json.");
        }
    }
}
