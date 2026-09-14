using System.Net;
using System.Net.Mail;
using BookingTracker.Domain.Enums;
using BookingTracker.Infrastructure.Email;
using BookingTracker.UnitTests.TestSupport;

namespace BookingTracker.UnitTests.Infrastructure.Email;

/// <summary>
/// Which SMTP failures are worth retrying.
///
/// Every case here drives a real System.Net.Mail.SmtpClient at a scripted
/// server rather than constructing an SmtpException by hand, because the point
/// is what the library actually produces. Constructed exceptions would only
/// confirm the classifier agrees with the test author's guess - and the guess
/// would have been wrong: SmtpClient silently ignores a 535 rejection of AUTH
/// and carries on unauthenticated, so a bad password surfaces as a 530 at
/// MAIL FROM, not as the 535 the server sent.
/// </summary>
public class SmtpFailureClassifierTests
{
    /// <summary>Sends one message at a scripted server and returns whatever it threw.</summary>
    private static async Task<Exception> CaptureFailureAsync(ScriptedSmtpServer server, bool withCredentials = false)
    {
        using var client = new SmtpClient("127.0.0.1", server.Port) { EnableSsl = false, Timeout = 5000 };
        if (withCredentials) client.Credentials = new NetworkCredential("user@example.com", "wrong-password");

        using var message = new MailMessage("from@example.com", "to@example.com", "Subject", "Body");
        return await Record.ExceptionAsync(() => client.SendMailAsync(message))
               ?? throw new InvalidOperationException("Expected the send to fail, but it succeeded.");
    }

    // ---- Permanent: authentication and configuration ------------------------

    [Theory]
    [InlineData(530, "5.7.0 Authentication Required")]        // the bad-App-Password case, as it actually surfaces
    [InlineData(530, "5.7.0 Must issue a STARTTLS command first")]
    [InlineData(534, "5.7.9 Authentication mechanism is too weak")]
    [InlineData(535, "5.7.8 Username and Password not accepted")]
    [InlineData(538, "5.7.11 Encryption required for requested authentication mechanism")]
    public async Task AnAuthenticationRejection_IsPermanent(int code, string text)
    {
        using var server = ScriptedSmtpServer.RejectingAt("MAIL", $"{code} {text}");

        var failure = await CaptureFailureAsync(server, withCredentials: true);

        Assert.Equal(EmailFailureKind.Permanent, SmtpFailureClassifier.Classify(failure));
    }

    [Fact]
    public async Task ARejectedCredential_ReachesTheClassifierAs530_NotAs535()
    {
        // Pinned because it is genuinely surprising and the whole classification
        // rests on it: SmtpClient does not throw when the server refuses AUTH.
        // It proceeds unauthenticated, and the server then refuses MAIL FROM.
        using var server = ScriptedSmtpServer.RejectingAt("AUTH", "535 5.7.8 Username and Password not accepted");

        using var client = new SmtpClient("127.0.0.1", server.Port) { EnableSsl = false, Timeout = 5000 };
        client.Credentials = new NetworkCredential("user@example.com", "wrong-password");
        using var message = new MailMessage("from@example.com", "to@example.com", "Subject", "Body");

        // The scripted server accepts everything else, so this send succeeds -
        // demonstrating that the 535 alone never reaches the caller at all.
        await client.SendMailAsync(message);
    }

    // ---- Transient: worth another attempt -----------------------------------

    [Theory]
    [InlineData(421, "4.7.0 Service not available, try again later")]
    [InlineData(451, "4.3.0 Temporary local error in processing")]
    [InlineData(452, "4.2.2 Insufficient system storage")]
    public async Task ATemporaryServerFailure_IsTransient(int code, string text)
    {
        using var server = ScriptedSmtpServer.RejectingAt("MAIL", $"{code} {text}");

        var failure = await CaptureFailureAsync(server);

        Assert.Equal(EmailFailureKind.Transient, SmtpFailureClassifier.Classify(failure));
    }

    [Fact]
    public async Task ATemporaryAuthenticationFailure_IsTransient()
    {
        // 454 reads like the permanent auth codes and is not one: 4xx means the
        // server could not verify right now, which is exactly what retrying is
        // for. Called out separately because it is the tempting wrong addition
        // to the permanent set.
        using var server = ScriptedSmtpServer.RejectingAt("MAIL", "454 4.7.0 Temporary authentication failure");

        var failure = await CaptureFailureAsync(server, withCredentials: true);

        Assert.Equal(EmailFailureKind.Transient, SmtpFailureClassifier.Classify(failure));
    }

    [Fact]
    public async Task AnUnreachableServer_IsTransient()
    {
        // Arrives as GeneralFailure (-1) wrapping a SocketException. This is the
        // failure retrying was built for, so it must never be classified away.
        using var client = new SmtpClient("127.0.0.1", 1) { EnableSsl = false, Timeout = 3000 };
        using var message = new MailMessage("from@example.com", "to@example.com", "Subject", "Body");

        var failure = await Record.ExceptionAsync(() => client.SendMailAsync(message));

        Assert.NotNull(failure);
        Assert.Equal(EmailFailureKind.Transient, SmtpFailureClassifier.Classify(failure));
    }

    [Fact]
    public async Task AnUnknownRecipient_StaysTransient()
    {
        // 550 is permanent in SMTP terms, but it is a recipient problem rather
        // than a configuration one and was not what the retry-budget fix was
        // about. Pinned so that broadening the permanent set to "any 5xx" is a
        // deliberate decision rather than an accident.
        using var server = ScriptedSmtpServer.RejectingAt("RCPT", "550 5.1.1 No such user here");

        var failure = await CaptureFailureAsync(server);

        Assert.IsType<SmtpFailedRecipientException>(failure);
        Assert.Equal(EmailFailureKind.Transient, SmtpFailureClassifier.Classify(failure));
    }

    [Fact]
    public void ANonSmtpException_IsTransient()
    {
        // A provider that is not SMTP at all (a future SendGrid/SES sender)
        // must not be silently classified as permanent by a classifier that
        // knows nothing about it.
        Assert.Equal(EmailFailureKind.Transient, SmtpFailureClassifier.Classify(new TimeoutException("timed out")));
        Assert.Equal(EmailFailureKind.Transient, SmtpFailureClassifier.Classify(new HttpRequestException("401")));
    }

    [Fact]
    public async Task ACleanSend_ThrowsNothing()
    {
        // The control: proves the scripted server is well-behaved, so a failure
        // in any case above is the reply code under test rather than the fake.
        using var server = ScriptedSmtpServer.Accepting();
        using var client = new SmtpClient("127.0.0.1", server.Port) { EnableSsl = false, Timeout = 5000 };
        using var message = new MailMessage("from@example.com", "to@example.com", "Subject", "Body");

        await client.SendMailAsync(message);
    }
}
