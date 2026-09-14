import { describe, expect, it } from 'vitest';
import {
  DEFAULT_WORKDAY_INTERVAL, MONDAY, canCopyDay, copyDayToWeekdays, emptyWeek, toWeeklyHours,
} from '../workingHours';
import type { WeeklyHours } from '../workingHours';
import { workingSchedule } from '../../test/factories';
import type { DayOfWeekNumber } from '../types';

/**
 * The weekly-hours model. `copyDayToWeekdays` is the one operation with a real
 * rule in it - which days it touches, and which it must leave alone - so it is
 * a function rather than three lines inside the form.
 */

const SATURDAY: DayOfWeekNumber = 6;
const SUNDAY: DayOfWeekNumber = 0;

function mondayNineToFive(): WeeklyHours {
  const week = emptyWeek();
  week[MONDAY] = {
    dayOfWeek: MONDAY,
    isEnabled: true,
    intervals: [{ ...DEFAULT_WORKDAY_INTERVAL }],
  };
  return week;
}

describe('emptyWeek', () => {
  it('has all seven days, every one closed', () => {
    const week = emptyWeek();

    expect(Object.keys(week)).toHaveLength(7);
    for (let day = 0; day <= 6; day++) {
      expect(week[day as DayOfWeekNumber].isEnabled).toBe(false);
      expect(week[day as DayOfWeekNumber].intervals).toEqual([]);
    }
  });
});

describe('toWeeklyHours', () => {
  it('fills in a day the server did not send, as closed', () => {
    const week = toWeeklyHours(
      workingSchedule({ days: [{ dayOfWeek: 3, isEnabled: true, intervals: [{ start: '10:00:00', end: '12:00:00' }] }] }),
    );

    expect(week[3].intervals).toEqual([{ start: '10:00:00', end: '12:00:00' }]);
    expect(week[SATURDAY]).toEqual({ dayOfWeek: SATURDAY, isEnabled: false, intervals: [] });
  });
});

describe('copyDayToWeekdays', () => {
  it('applies Monday to Tuesday through Friday', () => {
    const week = copyDayToWeekdays(mondayNineToFive(), MONDAY);

    for (const day of [2, 3, 4, 5] as DayOfWeekNumber[]) {
      expect(week[day].isEnabled).toBe(true);
      expect(week[day].intervals).toEqual([{ start: '09:00:00', end: '17:00:00' }]);
      expect(week[day].dayOfWeek).toBe(day);
    }
  });

  it('leaves the weekend exactly as it was', () => {
    // Opening Saturday because Monday is open would be the button doing
    // something nobody asked for - and the weekend is where hours differ.
    const before = mondayNineToFive();
    before[SATURDAY] = { dayOfWeek: SATURDAY, isEnabled: true, intervals: [{ start: '11:00:00', end: '13:00:00' }] };

    const week = copyDayToWeekdays(before, MONDAY);

    expect(week[SATURDAY]).toEqual(before[SATURDAY]);
    expect(week[SUNDAY]).toEqual(before[SUNDAY]);
  });

  it('leaves the source day untouched', () => {
    const before = mondayNineToFive();

    expect(copyDayToWeekdays(before, MONDAY)[MONDAY]).toEqual(before[MONDAY]);
  });

  it('replaces whatever a weekday already had rather than appending to it', () => {
    const before = mondayNineToFive();
    before[3] = { dayOfWeek: 3, isEnabled: true, intervals: [{ start: '08:00:00', end: '09:00:00' }] };

    expect(copyDayToWeekdays(before, MONDAY)[3].intervals).toEqual([{ start: '09:00:00', end: '17:00:00' }]);
  });

  it('gives every day its own interval objects', () => {
    // Two days sharing one object stays harmless only until something edits in
    // place, which is the same reason the backend clones an owned TimeRange.
    const week = copyDayToWeekdays(mondayNineToFive(), MONDAY);

    expect(week[2].intervals[0]).not.toBe(week[3].intervals[0]);
    expect(week[2].intervals[0]).not.toBe(week[MONDAY].intervals[0]);
  });

  it('copies a multi-interval day whole', () => {
    const before = emptyWeek();
    before[MONDAY] = {
      dayOfWeek: MONDAY,
      isEnabled: true,
      intervals: [{ start: '09:00:00', end: '12:00:00' }, { start: '13:00:00', end: '17:00:00' }],
    };

    expect(copyDayToWeekdays(before, MONDAY)[5].intervals).toHaveLength(2);
  });

  it('does not mutate the week it was given', () => {
    const before = mondayNineToFive();

    copyDayToWeekdays(before, MONDAY);

    expect(before[2].isEnabled).toBe(false);
  });
});

describe('canCopyDay', () => {
  it('is true for an open day with hours', () => {
    expect(canCopyDay(mondayNineToFive(), MONDAY)).toBe(true);
  });

  it('is false for a closed day, so the shortcut can never quietly close the week', () => {
    expect(canCopyDay(emptyWeek(), MONDAY)).toBe(false);
  });

  it('is false for a day switched on with no hours', () => {
    const week = emptyWeek();
    week[MONDAY] = { dayOfWeek: MONDAY, isEnabled: true, intervals: [] };

    expect(canCopyDay(week, MONDAY)).toBe(false);
  });
});

describe('DEFAULT_WORKDAY_INTERVAL', () => {
  it('mirrors the backend default a new schedule is seeded with', () => {
    // WorkingScheduleDefaults.DayStart/DayEnd in the Domain. If those move, a
    // row added by hand should offer the same hours, not a second guess.
    expect(DEFAULT_WORKDAY_INTERVAL).toEqual({ start: '09:00:00', end: '17:00:00' });
  });
});
