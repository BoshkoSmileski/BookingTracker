using BookingTracker.Domain.Entities;
using BookingTracker.Domain.ValueObjects;

namespace BookingTracker.Domain.Common;

/// <summary>
/// The weekly schedule a brand-new organizer starts on: Monday-Friday
/// 09:00-17:00, Saturday and Sunday closed.
///
/// One definition, in the Domain, for the same reason <see cref="BookingFieldLimits"/>
/// is one definition: the number would otherwise be written in the handler that
/// seeds it, in the frontend editor that offers it, and in whatever test claims
/// it - three copies free to drift. Application seeds a first schedule from
/// <see cref="CreateDays"/>; the frontend mirrors only the interval, in
/// <c>lib/workingHours.ts</c>, the way <c>lib/types.ts</c> mirrors a DTO.
///
/// These are the hours a schedule is CREATED with, never hours anything falls
/// back to. Once an organizer has a <see cref="WorkingSchedule"/>, whatever it
/// says is the whole truth - a day they switched off stays off, and nothing
/// here is consulted again.
/// </summary>
public static class WorkingScheduleDefaults
{
    public static readonly TimeOnly DayStart = new(9, 0);
    public static readonly TimeOnly DayEnd = new(17, 0);

    /// <summary>
    /// Used when an organizer's zone is unknown or unrecognized. UTC rather
    /// than a guess: a wrong zone silently shifts every slot the guest is
    /// offered, and UTC is at least an answer the organizer can recognize as
    /// needing correction on the Working hours screen.
    /// </summary>
    public const string FallbackTimeZoneId = "UTC";

    public static bool IsWorkingDay(DayOfWeek day) =>
        day is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

    /// <summary>
    /// All seven days for a newly created schedule - the five weekdays open
    /// 09:00-17:00, the weekend present but disabled and empty. Every day is
    /// materialized rather than only the open ones, so the editor loads a full
    /// week and "Saturday is closed" is a stored fact rather than an absence.
    /// </summary>
    public static IReadOnlyList<WorkingDay> CreateDays(Guid workingScheduleId) =>
        Enum.GetValues<DayOfWeek>()
            .Select(day => WorkingDay.Create(
                workingScheduleId,
                day,
                IsWorkingDay(day),
                // A fresh TimeRange per day, never one shared instance: EF Core
                // identifies owned types by reference, which is the trap
                // WorkingDay.AddInterval already guards against.
                IsWorkingDay(day) ? [TimeRange.Create(DayStart, DayEnd)] : []))
            .ToList();

    /// <summary>
    /// Whether this platform recognizes the id as a time zone. The one
    /// definition of that question, shared by the validator that rejects a
    /// typed zone and <see cref="ResolveTimeZoneId"/>, which falls back instead.
    /// </summary>
    public static bool IsKnownTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) return false;

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    /// <summary>
    /// The given zone if it is real, otherwise <see cref="FallbackTimeZoneId"/>.
    /// Used where a zone is a *hint* rather than an instruction - seeding a
    /// first schedule from the browser's resolved zone must never be able to
    /// fail the request that carried it.
    /// </summary>
    public static string ResolveTimeZoneId(string? timeZoneId) =>
        IsKnownTimeZone(timeZoneId) ? timeZoneId! : FallbackTimeZoneId;
}
