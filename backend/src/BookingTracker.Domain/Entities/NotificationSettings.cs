using BookingTracker.Domain.Common;
using BookingTracker.Domain.Exceptions;

namespace BookingTracker.Domain.Entities;

/// <summary>
/// One per organizer (unique index on OrganizerId, same "one-per-organizer"
/// shape as WorkingSchedule/CalendarConnection), controlling which automatic
/// notification emails go out for that organizer's bookings. Reminder
/// intervals are stored as a CSV of minutes-before-the-event
/// (ReminderMinutesBeforeEventCsv) rather than a child table - simple, and
/// already future-ready for "multiple reminders" since it's a list from day
/// one; parsing happens in the read-only ReminderMinutesBeforeEvent property.
/// </summary>
public sealed class NotificationSettings : Entity<Guid>
{
    public const int MinReminderMinutes = 5;
    public const int MaxReminderMinutes = 43200; // 30 days
    /// <summary>Raised from 5 to 8 when the settings UI moved to a fixed preset grid - it offers 8 checkboxes, so the cap must not make a valid-looking selection unsaveable.</summary>
    public const int MaxReminderIntervals = 8;
    public static readonly IReadOnlyList<int> DefaultReminderMinutesBeforeEvent = [1440]; // 24 hours

    public Guid OrganizerId { get; private set; }
    public bool NotifyGuestOnBooking { get; private set; }
    public bool NotifyOrganizerOnBooking { get; private set; }
    public bool RemindersEnabled { get; private set; }
    public string ReminderMinutesBeforeEventCsv { get; private set; } = default!;

    /// <summary>
    /// Copies the organizer in whenever a guest reminder goes out. Off by
    /// default: reminders fire per booking per configured interval, so leaving
    /// this on by default would multiply an organizer's inbox by the number of
    /// intervals they configured - an opt-in is the safe default, unlike the
    /// booking/cancellation notices which are one-per-event.
    /// </summary>
    public bool NotifyOrganizerOnReminderSent { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyList<int> ReminderMinutesBeforeEvent =>
        ReminderMinutesBeforeEventCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToList();

    private NotificationSettings() { }

    public static NotificationSettings CreateDefault(Guid organizerId) => new()
    {
        Id = Guid.NewGuid(),
        OrganizerId = organizerId,
        NotifyGuestOnBooking = true,
        NotifyOrganizerOnBooking = true,
        RemindersEnabled = true,
        ReminderMinutesBeforeEventCsv = ToCsv(DefaultReminderMinutesBeforeEvent),
        NotifyOrganizerOnReminderSent = false,
        CreatedAtUtc = DateTime.UtcNow,
        UpdatedAtUtc = DateTime.UtcNow,
    };

    public void UpdateSettings(
        bool notifyGuestOnBooking, bool notifyOrganizerOnBooking, bool remindersEnabled, IReadOnlyList<int> reminderMinutesBeforeEvent,
        bool notifyOrganizerOnReminderSent = false)
    {
        var distinctMinutes = reminderMinutesBeforeEvent.Distinct().OrderBy(m => m).ToList();

        if (remindersEnabled)
        {
            if (distinctMinutes.Count == 0)
                throw new DomainException("At least one reminder interval is required when reminders are enabled.");
            if (distinctMinutes.Count > MaxReminderIntervals)
                throw new DomainException($"No more than {MaxReminderIntervals} reminder intervals are supported.");
            if (distinctMinutes.Any(m => m < MinReminderMinutes || m > MaxReminderMinutes))
                throw new DomainException($"Reminder intervals must be between {MinReminderMinutes} and {MaxReminderMinutes} minutes.");
        }

        NotifyGuestOnBooking = notifyGuestOnBooking;
        NotifyOrganizerOnBooking = notifyOrganizerOnBooking;
        RemindersEnabled = remindersEnabled;
        NotifyOrganizerOnReminderSent = notifyOrganizerOnReminderSent;
        // Preserve whatever was configured even if reminders are currently disabled,
        // so re-enabling later doesn't silently reset to the default interval - unless
        // the caller passed an empty list while disabled, which just means "no opinion yet".
        if (distinctMinutes.Count > 0)
        {
            ReminderMinutesBeforeEventCsv = ToCsv(distinctMinutes);
        }
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string ToCsv(IReadOnlyList<int> minutes) => string.Join(',', minutes);
}
