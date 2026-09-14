using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using BookingTracker.Domain.Services;
using BookingTracker.Domain.ValueObjects;
using BookingTracker.UnitTests.TestSupport;

namespace BookingTracker.UnitTests.Domain.Services;

/// <summary>
/// SlotGenerationService is a pure function (no I/O), so every test here
/// constructs its inputs directly and asserts on the returned slots - no
/// database, no mocks. "Today" is fixed far in the past/future per test via
/// an explicit nowUtc argument so nothing here is sensitive to the actual
/// wall-clock date it happens to run on.
/// </summary>
public class SlotGenerationServiceTests
{
    private static readonly Guid ScheduleId = Guid.NewGuid();
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    // A Monday, chosen so DayOfWeek-based tests are unambiguous.
    private static readonly DateOnly Monday = new(2026, 8, 3);

    // Comfortably before any date used in these tests, so minNoticeMinutes=0
    // tests never trip the same-day/past-time cutoff by accident.
    private static readonly DateTime FarPastNowUtc = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static IReadOnlyList<AvailableSlot> Generate(
        IReadOnlyList<WorkingDay> days,
        IReadOnlyList<AvailabilityException>? exceptions = null,
        IReadOnlyList<OccupiedInterval>? bookings = null,
        TimeZoneInfo? timeZone = null,
        int durationMinutes = 30,
        int bufferBefore = 0,
        int bufferAfter = 0,
        DateOnly? from = null,
        DateOnly? to = null,
        DateTime? nowUtc = null,
        int stepMinutes = SlotGenerationService.DefaultSlotStepMinutes,
        int minNoticeMinutes = 0)
        => SlotGenerationService.GenerateSlots(
            days,
            exceptions ?? [],
            bookings ?? [],
            timeZone ?? Utc,
            durationMinutes,
            bufferBefore,
            bufferAfter,
            from ?? Monday,
            to ?? Monday,
            nowUtc ?? FarPastNowUtc,
            stepMinutes,
            minNoticeMinutes);

    // ---------------------------------------------------------------
    // Working hours
    // ---------------------------------------------------------------

    [Fact]
    public void WorkingHours_SingleInterval_GeneratesSlotsAcrossTheWholeInterval()
    {
        var days = new[] { WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Monday, (9, 0, 17, 0)) };

        var slots = Generate(days, durationMinutes: 30, stepMinutes: 15);

        Assert.Equal(new TimeOnly(9, 0), slots.First().StartTime);
        Assert.Equal(new TimeOnly(16, 30), slots.Last().StartTime);
        Assert.Equal(new TimeOnly(17, 0), slots.Last().EndTime);
    }

    [Fact]
    public void WorkingHours_DayNotEnabled_ProducesNoSlots()
    {
        var days = new[] { WorkingDayFactory.Disabled(ScheduleId, DayOfWeek.Monday) };

        var slots = Generate(days);

        Assert.Empty(slots);
    }

    [Fact]
    public void WorkingHours_DayNotPresentAtAll_ProducesNoSlots()
    {
        // Only Tuesday configured - Monday isn't in the list at all, not just disabled.
        var days = new[] { WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Tuesday, (9, 0, 17, 0)) };

        var slots = Generate(days, from: Monday, to: Monday);

        Assert.Empty(slots);
    }

    [Fact]
    public void WorkingHours_OnlyDaysMatchingTheRequestedDateOfWeek_ProduceSlots()
    {
        var days = new[]
        {
            WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Monday, (9, 0, 12, 0)),
            WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Tuesday, (9, 0, 12, 0)),
        };
        var tuesday = Monday.AddDays(1);

        var slots = Generate(days, from: Monday, to: tuesday);

        Assert.All(slots, s => Assert.True(s.Date == Monday || s.Date == tuesday));
        Assert.Contains(slots, s => s.Date == Monday);
        Assert.Contains(slots, s => s.Date == tuesday);
    }

    // ---------------------------------------------------------------
    // Multiple intervals / lunch breaks
    // ---------------------------------------------------------------

    [Fact]
    public void MultipleIntervals_LunchBreakGap_ProducesNoSlotsDuringTheGap()
    {
        var days = new[] { WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Monday, (9, 0, 12, 0), (13, 0, 17, 0)) };

        var slots = Generate(days, durationMinutes: 30, stepMinutes: 15);

        Assert.DoesNotContain(slots, s => s.StartTime >= new TimeOnly(12, 0) && s.StartTime < new TimeOnly(13, 0));
        Assert.Contains(slots, s => s.StartTime == new TimeOnly(11, 30) && s.EndTime == new TimeOnly(12, 0));
        Assert.Contains(slots, s => s.StartTime == new TimeOnly(13, 0));
    }

    [Fact]
    public void MultipleIntervals_ThreeIntervalsInOneDay_AllProduceSlotsIndependently()
    {
        var days = new[]
        {
            WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Monday, (8, 0, 10, 0), (12, 0, 13, 0), (15, 0, 18, 0)),
        };

        var slots = Generate(days, durationMinutes: 30, stepMinutes: 30);

        // 8:00-10:00 (120 min): starts 0,30,60,90 -> 4 slots. 12:00-13:00 (60 min): starts 0,30 -> 2 slots.
        // 15:00-18:00 (180 min): starts 0,30,...,150 -> 6 slots.
        Assert.Equal(4, slots.Count(s => s.StartTime >= new TimeOnly(8, 0) && s.StartTime < new TimeOnly(10, 0)));
        Assert.Equal(2, slots.Count(s => s.StartTime >= new TimeOnly(12, 0) && s.StartTime < new TimeOnly(13, 0)));
        Assert.Equal(6, slots.Count(s => s.StartTime >= new TimeOnly(15, 0) && s.StartTime < new TimeOnly(18, 0)));
    }

    // ---------------------------------------------------------------
    // Buffers
    // ---------------------------------------------------------------

    [Fact]
    public void Buffers_BufferBeforeAndAfter_ExpandTheOccupiedWindowAroundASlot()
    {
        var days = new[] { WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Monday, (9, 0, 17, 0)) };
        // An existing 10:00-10:30 booking (already expanded by its own buffers upstream).
        var existing = new[] { new OccupiedInterval(Monday, new TimeOnly(10, 0), new TimeOnly(10, 30)) };

        var slotsNoBuffer = Generate(days, bookings: existing, durationMinutes: 30, stepMinutes: 15, bufferBefore: 0, bufferAfter: 0);
        var slotsWithBuffer = Generate(days, bookings: existing, durationMinutes: 30, stepMinutes: 15, bufferBefore: 30, bufferAfter: 30);

        // Without a buffer, 9:30 (ends exactly at 10:00) and 10:30 (starts exactly when the booking ends) are bookable.
        Assert.Contains(slotsNoBuffer, s => s.StartTime == new TimeOnly(9, 30));
        Assert.Contains(slotsNoBuffer, s => s.StartTime == new TimeOnly(10, 30));

        // With a 30-minute buffer on both sides, those same candidate slots now fall inside the widened occupied window and disappear.
        Assert.DoesNotContain(slotsWithBuffer, s => s.StartTime == new TimeOnly(9, 30));
        Assert.DoesNotContain(slotsWithBuffer, s => s.StartTime == new TimeOnly(10, 30));
    }

    [Fact]
    public void Buffers_ClampedAtMidnight_DoNotThrowOrWrapToTheAdjacentDay()
    {
        var days = new[] { WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Monday, (0, 0, 1, 0)) };

        var slots = Generate(days, durationMinutes: 15, stepMinutes: 15, bufferBefore: 120, bufferAfter: 120);

        // Should not throw, and slots at the very start of the day are still valid candidates
        // (the buffer clamp only affects the occupied-interval check, not slot generation itself).
        Assert.NotEmpty(slots);
        Assert.All(slots, s => Assert.Equal(Monday, s.Date));
    }

    // ---------------------------------------------------------------
    // Meeting duration
    // ---------------------------------------------------------------

    [Theory]
    [InlineData(15, 32)] // 8h window / 15-min steps, last slot start+15<=480
    [InlineData(30, 31)]
    [InlineData(60, 29)]
    public void MeetingDuration_DifferentDurations_ProduceExpectedSlotCount(int durationMinutes, int expectedCount)
    {
        var days = new[] { WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Monday, (9, 0, 17, 0)) };

        var slots = Generate(days, durationMinutes: durationMinutes, stepMinutes: 15);

        Assert.Equal(expectedCount, slots.Count);
    }

    [Fact]
    public void MeetingDuration_LongerThanTheEntireInterval_ProducesNoSlots()
    {
        var days = new[] { WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Monday, (9, 0, 9, 30)) };

        var slots = Generate(days, durationMinutes: 45);

        Assert.Empty(slots);
    }

    [Fact]
    public void MeetingDuration_ExactlyFillsTheInterval_ProducesExactlyOneSlot()
    {
        var days = new[] { WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Monday, (9, 0, 10, 0)) };

        var slots = Generate(days, durationMinutes: 60);

        var slot = Assert.Single(slots);
        Assert.Equal(new TimeOnly(9, 0), slot.StartTime);
        Assert.Equal(new TimeOnly(10, 0), slot.EndTime);
    }

    // ---------------------------------------------------------------
    // Minimum notice
    // ---------------------------------------------------------------

    [Fact]
    public void MinimumNotice_ExcludesSlotsWithinTheNoticeWindow()
    {
        var days = new[] { WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Monday, (9, 0, 17, 0)) };
        // "Now" is Monday 09:00 UTC; 2 hours of notice required.
        var nowUtc = Monday.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc);

        var slots = Generate(days, nowUtc: nowUtc, minNoticeMinutes: 120, durationMinutes: 30, stepMinutes: 30);

        Assert.DoesNotContain(slots, s => s.StartTime < new TimeOnly(11, 0));
        Assert.Contains(slots, s => s.StartTime == new TimeOnly(11, 0));
    }

    [Fact]
    public void MinimumNotice_Zero_AllowsBookingRightUpToTheCurrentMoment()
    {
        var days = new[] { WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Monday, (9, 0, 17, 0)) };
        var nowUtc = Monday.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc);

        var slots = Generate(days, nowUtc: nowUtc, minNoticeMinutes: 0, durationMinutes: 30, stepMinutes: 30);

        Assert.Contains(slots, s => s.StartTime == new TimeOnly(9, 30));
    }

    // ---------------------------------------------------------------
    // Booking window (the service's own fromDate/toDate range)
    // ---------------------------------------------------------------

    [Fact]
    public void BookingWindow_OnlyGeneratesSlotsWithinFromDateAndToDateInclusive()
    {
        var days = Enumerable.Range(0, 7)
            .Select(i => WorkingDayFactory.Enabled(ScheduleId, (DayOfWeek)i, (9, 0, 10, 0)))
            .ToList();

        var slots = Generate(days, from: Monday, to: Monday.AddDays(2));

        Assert.All(slots, s => Assert.InRange(s.Date.DayNumber, Monday.DayNumber, Monday.AddDays(2).DayNumber));
        Assert.Contains(slots, s => s.Date == Monday);
        Assert.Contains(slots, s => s.Date == Monday.AddDays(2));
        Assert.DoesNotContain(slots, s => s.Date == Monday.AddDays(3));
    }

    [Fact]
    public void BookingWindow_ToDateBeforeFromDate_ProducesNoSlotsAndDoesNotThrow()
    {
        var days = new[] { WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Monday, (9, 0, 17, 0)) };

        var slots = Generate(days, from: Monday, to: Monday.AddDays(-1));

        Assert.Empty(slots);
    }

    // ---------------------------------------------------------------
    // Blocked dates (whole-day exceptions)
    // ---------------------------------------------------------------

    [Fact]
    public void BlockedDates_WholeDayException_ProducesNoSlotsThatDay()
    {
        var days = new[] { WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Monday, (9, 0, 17, 0)) };
        var exceptions = new[]
        {
            AvailabilityException.Create(Guid.NewGuid(), Monday, null, null, AvailabilityExceptionType.Holiday, "Public holiday"),
        };

        var slots = Generate(days, exceptions: exceptions);

        Assert.Empty(slots);
    }

    [Fact]
    public void BlockedDates_MultiDayException_BlocksEveryDayInTheInclusiveRange()
    {
        // A vacation is stored as ONE exception spanning a range, not one row per
        // day - so the boundary days matter: both ends are inclusive.
        var days = Enumerable.Range(0, 7)
            .Select(i => WorkingDayFactory.Enabled(ScheduleId, Monday.AddDays(i).DayOfWeek, (9, 0, 17, 0)))
            .ToArray();

        var vacation = AvailabilityException.Create(
            Guid.NewGuid(), Monday.AddDays(1), null, null, AvailabilityExceptionType.Vacation, "Summer break",
            endDate: Monday.AddDays(4));

        var slots = Generate(days, exceptions: [vacation], from: Monday, to: Monday.AddDays(6));

        var blockedDates = slots.Select(s => s.Date).Distinct().ToList();
        Assert.Contains(Monday, blockedDates);                       // day before the range
        Assert.Contains(Monday.AddDays(5), blockedDates);            // day after the range
        Assert.Contains(Monday.AddDays(6), blockedDates);
        for (var i = 1; i <= 4; i++)
        {
            Assert.DoesNotContain(Monday.AddDays(i), blockedDates);  // inclusive of both ends
        }
    }

    [Fact]
    public void BlockedDates_MultiDayPartialException_BlocksThoseHoursOnEveryDayOfTheRange()
    {
        var days = Enumerable.Range(0, 3)
            .Select(i => WorkingDayFactory.Enabled(ScheduleId, Monday.AddDays(i).DayOfWeek, (9, 0, 12, 0)))
            .ToArray();

        // A time window on a range means the same window each day - a recurring
        // commitment across a period, not one block spanning the whole period.
        var standup = AvailabilityException.Create(
            Guid.NewGuid(), Monday, new TimeOnly(9, 0), new TimeOnly(10, 0), AvailabilityExceptionType.Meeting, "Daily standup",
            endDate: Monday.AddDays(2));

        var slots = Generate(days, exceptions: [standup], from: Monday, to: Monday.AddDays(2));

        Assert.All(slots, s => Assert.True(s.StartTime >= new TimeOnly(10, 0)));
        // Every day still has its post-standup slots - the range did not remove the days.
        Assert.Equal(3, slots.Select(s => s.Date).Distinct().Count());
    }

    [Fact]
    public void BlockedDates_SingleDayException_StillBlocksExactlyOneDay()
    {
        // Ranges are additive: an exception created without an end date must behave
        // exactly as it did before ranges existed.
        var days = new[]
        {
            WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Monday, (9, 0, 12, 0)),
            WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Tuesday, (9, 0, 12, 0)),
        };
        var exception = AvailabilityException.Create(
            Guid.NewGuid(), Monday, null, null, AvailabilityExceptionType.Holiday, null);

        var slots = Generate(days, exceptions: [exception], from: Monday, to: Monday.AddDays(1));

        Assert.Equal(Monday.AddDays(1), Assert.Single(slots.Select(s => s.Date).Distinct()));
    }

    [Fact]
    public void BlockedDates_WholeDayExceptionOnOneDay_DoesNotAffectOtherDays()
    {
        var days = new[]
        {
            WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Monday, (9, 0, 12, 0)),
            WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Tuesday, (9, 0, 12, 0)),
        };
        var tuesday = Monday.AddDays(1);
        var exceptions = new[]
        {
            AvailabilityException.Create(Guid.NewGuid(), Monday, null, null, AvailabilityExceptionType.Vacation, null),
        };

        var slots = Generate(days, exceptions: exceptions, from: Monday, to: tuesday);

        Assert.DoesNotContain(slots, s => s.Date == Monday);
        Assert.Contains(slots, s => s.Date == tuesday);
    }

    // ---------------------------------------------------------------
    // Partial-day exceptions
    // ---------------------------------------------------------------

    [Fact]
    public void PartialDayException_BlocksOnlyTheExceptionWindow()
    {
        var days = new[] { WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Monday, (9, 0, 17, 0)) };
        var exceptions = new[]
        {
            AvailabilityException.Create(Guid.NewGuid(), Monday, new TimeOnly(10, 0), new TimeOnly(11, 0), AvailabilityExceptionType.Meeting, "Internal sync"),
        };

        var slots = Generate(days, exceptions: exceptions, durationMinutes: 30, stepMinutes: 15);

        Assert.DoesNotContain(slots, s => s.StartTime < new TimeOnly(11, 0) && s.EndTime > new TimeOnly(10, 0));
        Assert.Contains(slots, s => s.StartTime == new TimeOnly(9, 30) && s.EndTime == new TimeOnly(10, 0));
        Assert.Contains(slots, s => s.StartTime == new TimeOnly(11, 0));
    }

    [Fact]
    public void PartialDayException_MultipleExceptionsSameDay_BlockEachWindowIndependently()
    {
        var days = new[] { WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Monday, (9, 0, 17, 0)) };
        var organizerId = Guid.NewGuid();
        var exceptions = new[]
        {
            AvailabilityException.Create(organizerId, Monday, new TimeOnly(10, 0), new TimeOnly(10, 30), AvailabilityExceptionType.Meeting, null),
            AvailabilityException.Create(organizerId, Monday, new TimeOnly(14, 0), new TimeOnly(15, 0), AvailabilityExceptionType.Meeting, null),
        };

        var slots = Generate(days, exceptions: exceptions, durationMinutes: 30, stepMinutes: 30);

        Assert.DoesNotContain(slots, s => s.StartTime == new TimeOnly(10, 0));
        Assert.DoesNotContain(slots, s => s.StartTime == new TimeOnly(14, 0) || s.StartTime == new TimeOnly(14, 30));
        Assert.Contains(slots, s => s.StartTime == new TimeOnly(9, 0));
        Assert.Contains(slots, s => s.StartTime == new TimeOnly(15, 0));
    }

    // ---------------------------------------------------------------
    // Timezone handling
    // ---------------------------------------------------------------

    [Fact]
    public void Timezone_ConvertsLocalSlotToTheCorrectUtcInstant()
    {
        // A fixed, unambiguous non-UTC offset: Europe/Skopje is UTC+1 (CET, standard time) in January, no DST.
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Skopje");
        var januaryMonday = new DateOnly(2026, 1, 5);
        // Window fits exactly one 30-minute slot, to keep the assertion below unambiguous.
        var days = new[] { WorkingDayFactory.Enabled(ScheduleId, januaryMonday.DayOfWeek, (9, 0, 9, 30)) };

        var slots = Generate(days, timeZone: timeZone, from: januaryMonday, to: januaryMonday, durationMinutes: 30, stepMinutes: 30);

        var slot = Assert.Single(slots);
        Assert.Equal(new TimeOnly(9, 0), slot.StartTime);
        // 09:00 Europe/Skopje (UTC+1 in January, standard time) == 08:00 UTC.
        Assert.Equal(new DateTime(2026, 1, 5, 8, 0, 0, DateTimeKind.Utc), slot.StartUtc);
    }

    [Fact]
    public void Timezone_SameLocalWorkingHours_ProduceDifferentUtcInstantsForDifferentZones()
    {
        // Window fits exactly one 60-minute slot, so .Single() below is unambiguous.
        var days = new[] { WorkingDayFactory.Enabled(ScheduleId, Monday.DayOfWeek, (9, 0, 10, 0)) };

        var utcSlots = Generate(days, timeZone: TimeZoneInfo.Utc, durationMinutes: 60, stepMinutes: 30);
        var tokyoSlots = Generate(days, timeZone: TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo"), durationMinutes: 60, stepMinutes: 30);

        Assert.Equal(utcSlots.Single().StartTime, tokyoSlots.Single().StartTime); // same organizer-local wall clock time
        Assert.NotEqual(utcSlots.Single().StartUtc, tokyoSlots.Single().StartUtc); // different real-world instant
    }

    // ---------------------------------------------------------------
    // No overlapping slots / correct spacing
    // ---------------------------------------------------------------

    [Fact]
    public void NoOverlappingSlots_ConsecutiveSlotsAreExactlyOneStepApart_AndNeverOverlap()
    {
        var days = new[] { WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Monday, (9, 0, 12, 0)) };

        var slots = Generate(days, durationMinutes: 30, stepMinutes: 15).OrderBy(s => s.StartTime).ToList();

        for (var i = 1; i < slots.Count; i++)
        {
            var gap = slots[i].StartTime.ToTimeSpan() - slots[i - 1].StartTime.ToTimeSpan();
            Assert.Equal(TimeSpan.FromMinutes(15), gap);
            Assert.True(slots[i - 1].EndTime <= slots[i].StartTime || slots[i - 1].StartTime < slots[i].StartTime);
        }
    }

    [Fact]
    public void NoOverlappingSlots_NoGeneratedSlotOverlapsAnExistingBooking()
    {
        var days = new[] { WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Monday, (9, 0, 17, 0)) };
        var existing = new[] { new OccupiedInterval(Monday, new TimeOnly(12, 0), new TimeOnly(13, 0)) };

        var slots = Generate(days, bookings: existing, durationMinutes: 30, stepMinutes: 15);

        Assert.DoesNotContain(slots, s => s.StartTime < new TimeOnly(13, 0) && s.EndTime > new TimeOnly(12, 0));
    }

    // ---------------------------------------------------------------
    // Edge cases
    // ---------------------------------------------------------------

    [Fact]
    public void EdgeCase_ZeroOrNegativeDuration_ProducesNoSlots()
    {
        var days = new[] { WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Monday, (9, 0, 17, 0)) };

        Assert.Empty(Generate(days, durationMinutes: 0));
        Assert.Empty(Generate(days, durationMinutes: -15));
    }

    [Fact]
    public void EdgeCase_SameDayAsNow_ExcludesSlotsAtOrBeforeTheCurrentLocalTime()
    {
        var days = new[] { WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Monday, (9, 0, 17, 0)) };
        var nowUtc = Monday.ToDateTime(new TimeOnly(13, 0), DateTimeKind.Utc);

        var slots = Generate(days, nowUtc: nowUtc, from: Monday, to: Monday, durationMinutes: 30, stepMinutes: 30);

        Assert.DoesNotContain(slots, s => s.StartTime <= new TimeOnly(13, 0));
        Assert.Contains(slots, s => s.StartTime == new TimeOnly(13, 30));
    }

    [Fact]
    public void EdgeCase_FutureDate_IsNotAffectedByTodaysCutoff()
    {
        var days = new[] { WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Monday, (9, 0, 10, 0)) };
        var nowUtc = Monday.ToDateTime(new TimeOnly(23, 0), DateTimeKind.Utc); // "now" is Monday night
        var nextMonday = Monday.AddDays(7);

        var slots = Generate(days, nowUtc: nowUtc, from: nextMonday, to: nextMonday, durationMinutes: 30, stepMinutes: 30);

        Assert.Contains(slots, s => s.StartTime == new TimeOnly(9, 0));
    }

    [Fact]
    public void EdgeCase_ExistingBookingExactlyAdjacentToASlot_DoesNotBlockIt()
    {
        // A booking ending exactly when a slot starts (or starting exactly when one ends)
        // does not overlap it - Overlaps() uses strict inequalities.
        var days = new[] { WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Monday, (9, 0, 11, 0)) };
        var existing = new[] { new OccupiedInterval(Monday, new TimeOnly(9, 0), new TimeOnly(10, 0)) };

        var slots = Generate(days, bookings: existing, durationMinutes: 30, stepMinutes: 30);

        Assert.Contains(slots, s => s.StartTime == new TimeOnly(10, 0));
    }

    [Fact]
    public void EdgeCase_NoWorkingDaysAtAll_ProducesNoSlots()
    {
        var slots = Generate([]);

        Assert.Empty(slots);
    }

    [Fact]
    public void EdgeCase_MultiDayRangeMixesBlockedAndOpenDays_OnlyOpenDaysYieldSlots()
    {
        var days = new[]
        {
            WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Monday, (9, 0, 10, 0)),
            WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Tuesday, (9, 0, 10, 0)),
            WorkingDayFactory.Enabled(ScheduleId, DayOfWeek.Wednesday, (9, 0, 10, 0)),
        };
        var exceptions = new[]
        {
            AvailabilityException.Create(Guid.NewGuid(), Monday.AddDays(1), null, null, AvailabilityExceptionType.SickLeave, null),
        };

        var slots = Generate(days, exceptions: exceptions, from: Monday, to: Monday.AddDays(2), durationMinutes: 60);

        Assert.Contains(slots, s => s.Date == Monday);
        Assert.DoesNotContain(slots, s => s.Date == Monday.AddDays(1));
        Assert.Contains(slots, s => s.Date == Monday.AddDays(2));
    }
}
