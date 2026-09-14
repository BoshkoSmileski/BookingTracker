using BookingTracker.Infrastructure.Email;

namespace BookingTracker.UnitTests.Infrastructure.Email;

/// <summary>
/// The startup guard on SMTP configuration.
///
/// It exists because "UseSmtp: true with an empty Host" is close to invisible at
/// runtime: bookings still succeed (queueing is best-effort, sending is
/// out-of-band), and each notification instead burns its whole retry budget
/// before being marked permanently Failed. Failing at startup turns a silent,
/// unrecoverable loss into an error message naming the missing key.
/// </summary>
public class EmailSettingsTests
{
    private static EmailSettings Smtp(string? host = "smtp.example.com", string? fromEmail = "noreply@example.com") =>
        new() { UseSmtp = true, Host = host!, FromEmail = fromEmail! };

    [Fact]
    public void FileSystemMode_NeedsNoSmtpConfigurationAtAll()
    {
        // The development default: no host, no credentials, and that is correct -
        // FileSystemEmailService writes to disk and never opens a connection.
        var settings = new EmailSettings { UseSmtp = false, Host = string.Empty, FromEmail = string.Empty };

        settings.EnsureValidForSending();
    }

    [Fact]
    public void FullyConfiguredSmtp_IsAccepted() => Smtp().EnsureValidForSending();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SmtpWithoutAHost_FailsFastAndNamesTheKey(string? host)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Smtp(host: host).EnsureValidForSending());

        Assert.Contains("Email:Host", error.Message, StringComparison.Ordinal);
        // The message has to say how to get back to a working state, not just what is wrong.
        Assert.Contains("Email:UseSmtp to false", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SmtpWithoutAFromAddress_FailsFast()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Smtp(fromEmail: " ").EnsureValidForSending());

        Assert.Contains("Email:FromEmail", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingHostAndFrom_AreReportedTogether()
    {
        // One restart per missing key is a poor way to find out there were two.
        var error = Assert.Throws<InvalidOperationException>(
            () => Smtp(host: "", fromEmail: "").EnsureValidForSending());

        Assert.Contains("Email:Host", error.Message, StringComparison.Ordinal);
        Assert.Contains("Email:FromEmail", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CredentialsStayOptional()
    {
        // An unauthenticated local relay (MailHog, Papercut, smtp4dev) is a normal
        // development setup, so blank credentials must not be treated as an error.
        var settings = Smtp();
        settings.Username = null;
        settings.Password = null;

        settings.EnsureValidForSending();
    }

    [Fact]
    public void TheGuardNeverMentionsASecret()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Smtp(host: "").EnsureValidForSending());

        // Startup errors get pasted into issues and chat logs; the project's
        // "never log a raw secret" rule applies to them too.
        Assert.DoesNotContain("Password", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("User Secrets", error.Message, StringComparison.Ordinal);
    }
}
