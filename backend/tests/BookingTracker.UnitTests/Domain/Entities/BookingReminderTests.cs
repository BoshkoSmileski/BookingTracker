using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.Domain.Exceptions;

namespace BookingTracker.UnitTests.Domain.Entities;

public class BookingReminderTests
{
    private static readonly DateTime MeetingUtc = new(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc);

    private static BookingReminder Create(int minutesBefore = 1440) =>
        BookingReminder.Schedule(Guid.NewGuid(), Guid.NewGuid(), minutesBefore, MeetingUtc);

    [Fact]
    public void Schedule_DerivesSendTimeFromTheMeetingStart()
    {
        var reminder = Create(minutesBefore: 1440);

        Assert.Equal(MeetingUtc.AddHours(-24), reminder.ScheduledForUtc);
        Assert.Equal(MeetingUtc, reminder.MeetingStartsAtUtc);
        Assert.Equal(BookingReminderStatus.Scheduled, reminder.Status);
        Assert.Equal(ReminderChannel.Email, reminder.Channel);
        Assert.Null(reminder.EmailNotificationId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void Schedule_WithNonPositiveLeadTime_Throws(int minutes)
    {
        Assert.Throws<DomainException>(() => BookingReminder.Schedule(Guid.NewGuid(), Guid.NewGuid(), minutes, MeetingUtc));
    }

    [Fact]
    public void MarkQueued_LinksTheNotification_AndLeavesScheduled()
    {
        var reminder = Create();
        var notificationId = Guid.NewGuid();

        reminder.MarkQueued(notificationId);

        Assert.Equal(BookingReminderStatus.Queued, reminder.Status);
        Assert.Equal(notificationId, reminder.EmailNotificationId);
        Assert.NotNull(reminder.QueuedAtUtc);
    }

    [Fact]
    public void MarkQueued_Twice_Throws()
    {
        // The in-process half of the never-send-twice guarantee: even if a sweep
        // somehow saw the same row twice, the second transition is refused rather
        // than silently queueing a second email. (The database-level half is the
        // filtered unique index - see BookingReminderConfiguration.)
        var reminder = Create();
        reminder.MarkQueued(Guid.NewGuid());

        Assert.Throws<DomainException>(() => reminder.MarkQueued(Guid.NewGuid()));
    }

    [Fact]
    public void Cancel_OnAScheduledReminder_Resolves()
    {
        var reminder = Create();

        reminder.Cancel("Booking cancelled");

        Assert.Equal(BookingReminderStatus.Cancelled, reminder.Status);
        Assert.Equal("Booking cancelled", reminder.ResolutionReason);
    }

    [Fact]
    public void Cancel_OnAnAlreadyQueuedReminder_IsANoOp()
    {
        // Mail that is already queued cannot be recalled, so cancelling a booking
        // must not rewrite history into something that never happened.
        var reminder = Create();
        reminder.MarkQueued(Guid.NewGuid());

        reminder.Cancel("Booking cancelled");

        Assert.Equal(BookingReminderStatus.Queued, reminder.Status);
        Assert.Null(reminder.ResolutionReason);
    }

    [Fact]
    public void Skip_OnAScheduledReminder_Resolves()
    {
        var reminder = Create();

        reminder.Skip("Missed by more than the grace period");

        Assert.Equal(BookingReminderStatus.Skipped, reminder.Status);
        Assert.Contains("grace period", reminder.ResolutionReason);
    }

    [Fact]
    public void IsDue_OnlyOnceTheSendTimeHasArrived_AndOnlyWhileScheduled()
    {
        var reminder = Create(minutesBefore: 1440);
        var sendTime = MeetingUtc.AddHours(-24);

        Assert.False(reminder.IsDue(sendTime.AddMinutes(-1)));
        Assert.True(reminder.IsDue(sendTime));
        Assert.True(reminder.IsDue(sendTime.AddMinutes(30)));

        reminder.MarkQueued(Guid.NewGuid());
        Assert.False(reminder.IsDue(sendTime.AddMinutes(30)));
    }
}
