namespace BookingTracker.Domain.Enums;

/// <summary>
/// Labels WHY a date/time is blocked. Every value behaves identically during
/// slot generation (the window is simply excluded) - this only drives how the
/// organizer's dashboard/editor displays the entry. Holiday is included here
/// rather than as a separate entity since a holiday is, functionally, just a
/// full-day blocked period.
/// </summary>
public enum AvailabilityExceptionType
{
    Holiday = 0,
    Vacation = 1,
    Meeting = 2,
    SickLeave = 3,
    Training = 4,
    Other = 5
}
