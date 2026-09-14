using System.Net.Mail;
using BookingTracker.Domain.Enums;

namespace BookingTracker.Infrastructure.Email;

/// <summary>
/// Decides whether a failed send is worth retrying, from the SMTP reply code
/// the server actually gave. The queue processor asks this before recording an
/// attempt, so a rejected credential fails immediately instead of spending the
/// whole retry budget - roughly a quarter of an hour of backoff - to arrive at
/// the same answer with the email lost anyway.
///
/// Two rules govern anything added here:
///
///  - Classify on the numeric reply code, never on the message text. The codes
///    are protocol-defined (RFC 5321 s4.2) and identical on every server and in
///    every locale; the message is prose that Microsoft, the server operator
///    and the user's culture can all change independently.
///
///  - Anything unrecognised is Transient. This class can only ever make the
///    queue give up EARLIER than it used to, so an unlisted code must keep the
///    exact behaviour the queue has always had. Being wrong in that direction
///    costs a few pointless retries; being wrong in the other direction throws
///    an email away that would have gone out.
///
/// The permanent set is deliberately confined to the 5xx AUTHENTICATION codes.
/// Other permanent 5xx replies (550 unknown mailbox, 552 over quota, 553 bad
/// sender) are left retrying exactly as before: they are recipient problems
/// rather than configuration ones, they are not what the retry-budget bug was
/// about, and narrowing the change to the reported fault is what keeps it a
/// reliability fix rather than a behaviour change.
/// </summary>
public static class SmtpFailureClassifier
{
    /// <summary>
    /// The 5xx replies that mean "this session is not permitted to send mail,
    /// and sending the same message again with the same configuration will be
    /// refused identically".
    ///
    /// Every one of these was confirmed against System.Net.Mail.SmtpClient
    /// driven at a scripted SMTP server, rather than read off the SmtpStatusCode
    /// enum - which matters, because only 530 has a name there. 534/535/538 are
    /// returned as unnamed casts, and comparing against the enum's named members
    /// alone would silently miss all three.
    /// </summary>
    private static readonly int[] PermanentAuthenticationCodes =
    [
        530, // Authentication required (also "must issue STARTTLS first" - both are configuration)
        534, // Authentication mechanism is too weak
        535, // Authentication credentials invalid - the rejected App Password case
        538, // Encryption required for the requested authentication mechanism
    ];

    /// <summary>
    /// 454 is the one authentication reply that is genuinely temporary (a 4xx
    /// by definition - the server is rate-limiting or momentarily unable to
    /// verify), so it must keep retrying. Called out only because it reads like
    /// the codes above and would otherwise be a tempting addition to them.
    /// </summary>
    public static EmailFailureKind Classify(Exception exception)
    {
        // SmtpFailedRecipientException derives from SmtpException, so this also
        // covers per-recipient rejections - which stay Transient under the rule
        // above, since none of their codes are in the permanent set.
        if (exception is not SmtpException smtp) return EmailFailureKind.Transient;

        // Connection refused, DNS failure and timeouts all arrive as
        // GeneralFailure (-1), usually wrapping a SocketException. Not in the
        // set, so they keep retrying - which is exactly right, since those are
        // the failures retrying was built for.
        return Array.IndexOf(PermanentAuthenticationCodes, (int)smtp.StatusCode) >= 0
            ? EmailFailureKind.Permanent
            : EmailFailureKind.Transient;
    }
}
