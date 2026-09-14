using BookingTracker.Domain.Common;

namespace BookingTracker.Domain.Entities;

/// <summary>
/// One per organizer (reused across all of their booking pages, the way a
/// real availability schedule works). Holds only the organizer's
/// timezone - the actual weekly hours live in WorkingDay rows referencing
/// this schedule by id, following the same "aggregates reference each other
/// by id, no navigation properties" pattern already used for BookingPage/
/// BookingSession rather than an owned collection.
/// </summary>
public class WorkingSchedule : Entity<Guid>
{
    public Guid OrganizerId { get; private set; }
    public string TimeZoneId { get; private set; } = default!;
    public DateTime CreatedAt { get; private set; }

    private WorkingSchedule() { }

    public static WorkingSchedule Create(Guid organizerId, string timeZoneId)
    {
        return new WorkingSchedule
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizerId,
            TimeZoneId = timeZoneId,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void ChangeTimeZone(string timeZoneId) => TimeZoneId = timeZoneId;
}
