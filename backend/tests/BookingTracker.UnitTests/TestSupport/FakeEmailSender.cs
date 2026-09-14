using BookingTracker.Application.Common.Interfaces;

namespace BookingTracker.UnitTests.TestSupport;

/// <summary>
/// An <see cref="IEmailSender"/> whose next outcome a test chooses. Hand-written
/// rather than mocked, like every other fake in this project - the interface is
/// one method, so a library would add a dependency and nothing else.
///
/// The failure is supplied as a real exception instance so a test can hand it
/// the genuine SmtpException a ScriptedSmtpServer produced, keeping the queue
/// processor's classification honest end to end instead of asserting against a
/// stand-in for the thing being classified.
/// </summary>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly Queue<Exception?> _outcomes = new();

    /// <summary>Every message handed to SendAsync, in order, including ones that then failed.</summary>
    public List<EmailMessage> Sent { get; } = [];

    public int SendCount { get; private set; }

    /// <summary>The outcome for any send once the queued outcomes run out. Null means success.</summary>
    public Exception? DefaultOutcome { get; set; }

    public static FakeEmailSender AlwaysSucceeds() => new();

    public static FakeEmailSender AlwaysFailsWith(Exception exception) => new() { DefaultOutcome = exception };

    /// <summary>Queues one outcome for the next send; null means that send succeeds.</summary>
    public FakeEmailSender ThenFailsWith(Exception? exception)
    {
        _outcomes.Enqueue(exception);
        return this;
    }

    public FakeEmailSender ThenSucceeds() => ThenFailsWith(null);

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        SendCount++;
        Sent.Add(message);

        var outcome = _outcomes.Count > 0 ? _outcomes.Dequeue() : DefaultOutcome;
        return outcome is null ? Task.CompletedTask : Task.FromException(outcome);
    }
}
