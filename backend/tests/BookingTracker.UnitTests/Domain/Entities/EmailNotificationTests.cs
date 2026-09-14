using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.Domain.Exceptions;

namespace BookingTracker.UnitTests.Domain.Entities;

/// <summary>
/// Exercises the retry/backoff policy directly on the domain entity - see
/// EmailQueueProcessor's doc comment for why the backoff math lives here
/// rather than in the (untested-in-isolation, like every other
/// BackgroundService in this project) queue processor itself.
/// </summary>
public class EmailNotificationTests
{
    private static EmailNotification CreatePending(int maxAttempts = 3) => EmailNotification.Create(
        Guid.NewGuid(), EmailNotificationType.BookingConfirmation, "guest@example.com", "Jane Doe",
        "Subject", "<p>html</p>", "text", "BookingConfirmation", maxAttempts);

    [Fact]
    public void Create_StartsPending_WithZeroAttempts()
    {
        var notification = CreatePending();

        Assert.Equal(EmailNotificationStatus.Pending, notification.Status);
        Assert.Equal(0, notification.AttemptCount);
        Assert.Null(notification.SentAtUtc);
    }

    [Fact]
    public void Create_WithNoRecipient_Throws()
    {
        Assert.Throws<DomainException>(() => EmailNotification.Create(
            Guid.NewGuid(), EmailNotificationType.BookingConfirmation, "", "Jane Doe", "Subject", "html", "text", "BookingConfirmation", 3));
    }

    [Fact]
    public void Create_WithZeroMaxAttempts_Throws()
    {
        Assert.Throws<DomainException>(() => EmailNotification.Create(
            Guid.NewGuid(), EmailNotificationType.BookingConfirmation, "guest@example.com", "Jane Doe", "Subject", "html", "text", "BookingConfirmation", 0));
    }

    [Fact]
    public void MarkSent_SetsStatusAndTimestamp_AndClearsAnyPriorError()
    {
        var notification = CreatePending();
        notification.RecordFailedAttempt("transient SMTP error", TimeSpan.FromSeconds(1));

        notification.MarkSent();

        Assert.Equal(EmailNotificationStatus.Sent, notification.Status);
        Assert.NotNull(notification.SentAtUtc);
        Assert.Null(notification.LastError);
    }

    [Fact]
    public void RecordFailedAttempt_BelowMaxAttempts_StaysPending_AndSchedulesARetry()
    {
        var notification = CreatePending(maxAttempts: 3);
        var before = DateTime.UtcNow;

        notification.RecordFailedAttempt("connection refused", TimeSpan.FromSeconds(60));

        Assert.Equal(EmailNotificationStatus.Pending, notification.Status);
        Assert.Equal(1, notification.AttemptCount);
        Assert.Equal("connection refused", notification.LastError);
        // First retry: base delay (60s) x 2^0 = 60s.
        Assert.InRange(notification.NextAttemptAtUtc, before.AddSeconds(59), before.AddSeconds(65));
    }

    [Fact]
    public void RecordFailedAttempt_BacksOffExponentially()
    {
        var notification = CreatePending(maxAttempts: 5);
        var baseDelay = TimeSpan.FromSeconds(10);

        notification.RecordFailedAttempt("err1", baseDelay); // 1st retry: 10s
        var afterFirst = notification.NextAttemptAtUtc;

        notification.RecordFailedAttempt("err2", baseDelay); // 2nd retry: 20s
        var afterSecond = notification.NextAttemptAtUtc;

        notification.RecordFailedAttempt("err3", baseDelay); // 3rd retry: 40s
        var afterThird = notification.NextAttemptAtUtc;

        var firstGap = afterFirst - DateTime.UtcNow;
        var secondGap = afterSecond - DateTime.UtcNow;
        var thirdGap = afterThird - DateTime.UtcNow;

        // Each successive gap should be roughly double the previous one (exponential backoff).
        Assert.True(secondGap > firstGap * 1.5, $"Expected second gap ({secondGap}) to be roughly double the first ({firstGap}).");
        Assert.True(thirdGap > secondGap * 1.5, $"Expected third gap ({thirdGap}) to be roughly double the second ({secondGap}).");
    }

    [Fact]
    public void RecordFailedAttempt_AtMaxAttempts_MarksPermanentlyFailed()
    {
        var notification = CreatePending(maxAttempts: 2);

        notification.RecordFailedAttempt("attempt 1 failed", TimeSpan.FromSeconds(1));
        Assert.Equal(EmailNotificationStatus.Pending, notification.Status);

        notification.RecordFailedAttempt("attempt 2 failed", TimeSpan.FromSeconds(1));

        Assert.Equal(EmailNotificationStatus.Failed, notification.Status);
        Assert.Equal(2, notification.AttemptCount);
    }

    [Fact]
    public void RecordFailedAttempt_DefaultsToTransient_SoAnUnclassifiedFailureStillRetries()
    {
        // The default matters: it is what every pre-existing caller gets, and
        // being wrong here would silently stop retrying failures that used to
        // recover on their own.
        var notification = CreatePending(maxAttempts: 3);

        notification.RecordFailedAttempt("something went wrong", TimeSpan.FromSeconds(1));

        Assert.Equal(EmailNotificationStatus.Pending, notification.Status);
    }

    [Fact]
    public void RecordFailedAttempt_WhenPermanent_FailsImmediatelyWithoutSpendingTheBudget()
    {
        // A rejected credential cannot succeed on a retry, so burning five
        // attempts over ~15 minutes of backoff reaches the same outcome later
        // and no more usefully.
        var notification = CreatePending(maxAttempts: 5);

        notification.RecordFailedAttempt("530 Authentication Required", TimeSpan.FromSeconds(60), EmailFailureKind.Permanent);

        Assert.Equal(EmailNotificationStatus.Failed, notification.Status);
        Assert.Equal(1, notification.AttemptCount);
    }

    [Fact]
    public void RecordFailedAttempt_WhenPermanent_StillRecordsTheAttemptAndTheError()
    {
        // Not retrying is not the same as not having tried: the row has to say
        // honestly that a send was attempted and failed, and why.
        var notification = CreatePending(maxAttempts: 5);

        notification.RecordFailedAttempt("530 Authentication Required", TimeSpan.FromSeconds(60), EmailFailureKind.Permanent);

        Assert.Equal("530 Authentication Required", notification.LastError);
        Assert.Equal(1, notification.AttemptCount);
        Assert.Null(notification.SentAtUtc);
    }

    [Fact]
    public void RecordFailedAttempt_WhenPermanent_AfterEarlierTransientOnes_FailsAtOnce()
    {
        // A server that was flaky and then rejected the credential: the earlier
        // attempts are kept, but the permanent one ends it there rather than
        // running out the remaining budget.
        var notification = CreatePending(maxAttempts: 5);
        notification.RecordFailedAttempt("timeout", TimeSpan.FromSeconds(1));
        notification.RecordFailedAttempt("timeout", TimeSpan.FromSeconds(1));
        Assert.Equal(EmailNotificationStatus.Pending, notification.Status);

        notification.RecordFailedAttempt("530 Authentication Required", TimeSpan.FromSeconds(1), EmailFailureKind.Permanent);

        Assert.Equal(EmailNotificationStatus.Failed, notification.Status);
        Assert.Equal(3, notification.AttemptCount);
    }

    [Fact]
    public void RecordFailedAttempt_TruncatesAVeryLongErrorMessage()
    {
        var notification = CreatePending();
        var hugeError = new string('x', 5000);

        notification.RecordFailedAttempt(hugeError, TimeSpan.FromSeconds(1));

        Assert.Equal(1000, notification.LastError!.Length);
    }
}
