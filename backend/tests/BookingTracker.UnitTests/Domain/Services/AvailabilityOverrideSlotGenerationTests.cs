using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.Domain.Services;
using BookingTracker.Domain.ValueObjects;
using BookingTracker.UnitTests.TestSupport;

namespace BookingTracker.UnitTests.Domain.Services;

/// <summary>
/// The precedence rules that make date-specific hours work, exercised against
/// the pure slot algorithm with no database.
///
/// A separate file from SlotGenerationServiceTests for the same reason
/// BookingSessionEventSourcingTests is separate from BookingSessionTests: this
/// is one specific claim about the service - which producer supplies a day's
/// open hours, and what still applies afterwards - rather than more coverage of
/// the slot maths itself.
/// </summary>
public class AvailabilityOverrideSlotGenerationTests
{
    private static readonly Guid ScheduleId = Guid.NewGuid();
    private static readonly Guid OrganizerId = Guid.NewGuid();
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    // A Monday and the Saturday of the same week, so weekday-vs-weekend
    // precedence is unambiguous.
    private static readonly DateOnly Monday = new(2026, 8, 3);
    private static readonly DateOnly Saturday = new(2026, 8, 8);

    private static readonly DateTime FarPastNowUtc = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Mon-Fri 09:00-17:00 - the schedule every example in the brief starts from.</summary>
    private static IReadOnlyList<WorkingDay> WeekdaysNineToFive() =>
    [
        WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Monday, (9, 0, 17, 0)),
        WorkingDayFactory.Disabled(ScheduleId, DayOfWeek.Saturday),
    ];

    private static IReadOnlyList<AvailableSlot> Generate(
        IReadOnlyList<AvailabilityOverride>? overrides = null,
        IReadOnlyList<AvailabilityException>? exceptions = null,
        IReadOnlyList<OccupiedInterval>? bookings = null,
        DateOnly? from = null,
        DateOnly? to = null,
        int durationMinutes = 60,
        int stepMinutes = 60)
        => SlotGenerationService.GenerateSlots(
            WeekdaysNineToFive(),
            exceptions ?? [],
            bookings ?? [],
            Utc,
            durationMinutes,
            bufferBeforeMinutes: 0,
            bufferAfterMinutes: 0,
            from ?? Monday,
            to ?? Monday,
            FarPastNowUtc,
            stepMinutes,
            minNoticeMinutes: 0,
            overrides: overrides);

    private static IEnumerable<TimeOnly> StartsOn(IReadOnlyList<AvailableSlot> slots, DateOnly date) =>
        slots.Where(s => s.Date == date).Select(s => s.StartTime);

    // ---------- the override replaces weekly hours ----------

    [Fact]
    public void NoOverride_UsesTheWeeklySchedule()
    {
        var slots = Generate();

        Assert.Equal(
            [new TimeOnly(9, 0), new TimeOnly(10, 0), new TimeOnly(11, 0), new TimeOnly(12, 0),
             new TimeOnly(13, 0), new TimeOnly(14, 0), new TimeOnly(15, 0), new TimeOnly(16, 0)],
            StartsOn(slots, Monday));
    }

    [Fact]
    public void AnOverride_ReplacesTheWeeklyHoursEntirelyRatherThanNarrowingThem()
    {
        // "Normally 09:00-17:00; tomorrow 13:00-17:00" - the brief's first example.
        var slots = Generate([WorkingDayFactory.Override(OrganizerId, Monday, (13, 0, 17, 0))]);

        Assert.Equal(
            [new TimeOnly(13, 0), new TimeOnly(14, 0), new TimeOnly(15, 0), new TimeOnly(16, 0)],
            StartsOn(slots, Monday));
    }

    [Fact]
    public void AnOverride_CanOpenHoursTheWeeklyScheduleNeverHad()
    {
        // The case no subtractive model could express: the override runs
        // 07:00-09:00, entirely OUTSIDE the weekly 09:00-17:00. If overrides
        // narrowed rather than replaced, this would produce nothing.
        var slots = Generate([WorkingDayFactory.Override(OrganizerId, Monday, (7, 0, 9, 0))]);

        Assert.Equal([new TimeOnly(7, 0), new TimeOnly(8, 0)], StartsOn(slots, Monday));
    }

    [Fact]
    public void AnOverride_OpensADayTheWeeklyScheduleHasDisabled()
    {
        // "Saturday: 09:00-12:00" against a Mon-Fri schedule. This is the exact
        // case the old `if (workingDay is null) continue;` made unreachable.
        var slots = Generate(
            [WorkingDayFactory.Override(OrganizerId, Saturday, (9, 0, 12, 0))],
            from: Saturday, to: Saturday);

        Assert.Equal([new TimeOnly(9, 0), new TimeOnly(10, 0), new TimeOnly(11, 0)], StartsOn(slots, Saturday));
    }

    [Fact]
    public void AClosedOverride_ClosesANormallyOpenDay()
    {
        // "Christmas Eve: closed" - an override with no ranges.
        var slots = Generate([WorkingDayFactory.ClosedOverride(OrganizerId, Monday)]);

        Assert.Empty(slots);
    }

    [Fact]
    public void MultipleRanges_ProduceSlotsInEachWithTheGapLeftClosed()
    {
        // "Aug 20: 09:00-12:00 and 13:00-15:00" - the brief's third example.
        var slots = Generate([WorkingDayFactory.Override(OrganizerId, Monday, (9, 0, 12, 0), (13, 0, 15, 0))]);

        Assert.Equal(
            [new TimeOnly(9, 0), new TimeOnly(10, 0), new TimeOnly(11, 0), new TimeOnly(13, 0), new TimeOnly(14, 0)],
            StartsOn(slots, Monday));
    }

    [Fact]
    public void AnOverride_AppliesOnlyToItsOwnDate()
    {
        var tuesday = Monday.AddDays(1);
        var days = new List<WorkingDay>
        {
            WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Monday, (9, 0, 17, 0)),
            WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Tuesday, (9, 0, 17, 0)),
        };

        var slots = SlotGenerationService.GenerateSlots(
            days, [], [], Utc, 60, 0, 0, Monday, tuesday, FarPastNowUtc, 60, 0,
            overrides: [WorkingDayFactory.Override(OrganizerId, Monday, (13, 0, 17, 0))]);

        Assert.Equal(new TimeOnly(13, 0), StartsOn(slots, Monday).First());
        Assert.Equal(new TimeOnly(9, 0), StartsOn(slots, tuesday).First());
    }

    // ---------- what still applies on top of an override ----------

    [Fact]
    public void AWholeDayBlock_StillClosesADayAnOverrideOpened()
    {
        // An override says when the day is OPEN; it does not un-block a blocked
        // date. Subtraction runs after resolution, whichever producer won.
        var slots = Generate(
            overrides: [WorkingDayFactory.Override(OrganizerId, Saturday, (9, 0, 12, 0))],
            exceptions: [AvailabilityException.Create(OrganizerId, Saturday, null, null, AvailabilityExceptionType.Holiday, null)],
            from: Saturday, to: Saturday);

        Assert.Empty(slots);
    }

    [Fact]
    public void ATimedBlock_IsSubtractedFromOverrideHoursExactlyAsFromWeeklyHours()
    {
        var slots = Generate(
            overrides: [WorkingDayFactory.Override(OrganizerId, Monday, (9, 0, 17, 0))],
            exceptions:
            [
                AvailabilityException.Create(
                    OrganizerId, Monday, new TimeOnly(11, 0), new TimeOnly(13, 0), AvailabilityExceptionType.Meeting, null)
            ]);

        Assert.Equal(
            [new TimeOnly(9, 0), new TimeOnly(10, 0), new TimeOnly(13, 0), new TimeOnly(14, 0),
             new TimeOnly(15, 0), new TimeOnly(16, 0)],
            StartsOn(slots, Monday));
    }

    [Fact]
    public void ExistingBookings_StillRemoveSlotsFromOverrideHours()
    {
        var slots = Generate(
            overrides: [WorkingDayFactory.Override(OrganizerId, Monday, (13, 0, 17, 0))],
            bookings: [OccupiedInterval.FromBookingWindow(Monday, new TimeOnly(14, 0), 60, 0, 0)]);

        Assert.DoesNotContain(new TimeOnly(14, 0), StartsOn(slots, Monday));
        Assert.Contains(new TimeOnly(13, 0), StartsOn(slots, Monday));
    }

    [Fact]
    public void MinimumNotice_StillAppliesToOverrideHours()
    {
        // "Now" is 10:00 on the override's own date with 4 hours' notice
        // required, so only slots from 14:00 survive.
        var nowUtc = new DateTime(Monday.Year, Monday.Month, Monday.Day, 10, 0, 0, DateTimeKind.Utc);

        var slots = SlotGenerationService.GenerateSlots(
            WeekdaysNineToFive(), [], [], Utc, 60, 0, 0, Monday, Monday, nowUtc, 60,
            minNoticeMinutes: 240,
            overrides: [WorkingDayFactory.Override(OrganizerId, Monday, (9, 0, 17, 0))]);

        Assert.Equal([new TimeOnly(14, 0), new TimeOnly(15, 0), new TimeOnly(16, 0)], StartsOn(slots, Monday));
    }

    [Fact]
    public void AnOverrideShorterThanTheService_ProducesNothing()
    {
        // A 30-minute opening cannot host a 60-minute booking - the slot loop's
        // existing rule, unchanged by which producer supplied the hours.
        var slots = Generate([WorkingDayFactory.Override(OrganizerId, Monday, (9, 0, 9, 30))], durationMinutes: 60);

        Assert.Empty(slots);
    }
}
