import type { DayOfWeekNumber, TimeRangeDto, WorkingDayDto, WorkingScheduleDto } from './types';

/**
 * The weekly-hours editor's model and the pure operations on it.
 *
 * Separate from the screen so "copy Monday across" is a function with a test
 * rather than three lines of state juggling inside a form, and so the one
 * interval a new row starts on has a name instead of being written inline.
 */

/** Every day of the week, keyed by its .NET Sunday-zero index - the shape the editor holds. */
export type WeeklyHours = Record<DayOfWeekNumber, WorkingDayDto>;

/**
 * The hours a new interval - and a newly created schedule - starts on.
 *
 * Mirrors `WorkingScheduleDefaults.DayStart`/`DayEnd` in the Domain, the way
 * `lib/types.ts` mirrors a DTO. The backend is the one that actually seeds a
 * first schedule (on the organizer's first booking page); this exists so that
 * adding a row by hand offers the same hours rather than a second, unrelated
 * guess at what a working day looks like.
 */
export const DEFAULT_WORKDAY_INTERVAL: TimeRangeDto = { start: '09:00:00', end: '17:00:00' };

/** Monday-Friday, the days "copy across" applies to. The weekend is deliberately not one of them - see copyDayToWeekdays. */
export const WEEKDAYS: DayOfWeekNumber[] = [1, 2, 3, 4, 5];

export const MONDAY: DayOfWeekNumber = 1;

/** Seven closed days, the state the editor holds before a schedule arrives (or when the organizer has none). */
export function emptyWeek(): WeeklyHours {
  const week = {} as WeeklyHours;
  for (let day = 0; day <= 6; day++) {
    week[day as DayOfWeekNumber] = { dayOfWeek: day as DayOfWeekNumber, isEnabled: false, intervals: [] };
  }
  return week;
}

/** A saved schedule as the editor holds it: every day present, whether the server sent it or not. */
export function toWeeklyHours(schedule: WorkingScheduleDto): WeeklyHours {
  const week = emptyWeek();
  for (const day of schedule.days) week[day.dayOfWeek] = day;
  return week;
}

/**
 * Monday's hours applied to Tuesday-Friday.
 *
 * Weekdays only, not the whole week. "The same every day I work" is the common
 * shape and the one worth a shortcut; silently opening Saturday because Monday
 * is open would be the button doing something nobody asked it to, and the
 * weekend is exactly where an organizer's hours differ.
 *
 * Intervals are cloned per day. Nothing here mutates one, but two days sharing
 * an object is the kind of thing that only stays harmless until someone edits
 * in place - the same reason `WorkingDay.AddInterval` clones on the backend.
 */
export function copyDayToWeekdays(week: WeeklyHours, source: DayOfWeekNumber): WeeklyHours {
  const from = week[source];
  const next = { ...week };

  for (const day of WEEKDAYS) {
    if (day === source) continue;
    next[day] = {
      dayOfWeek: day,
      isEnabled: from.isEnabled,
      intervals: from.intervals.map((interval) => ({ ...interval })),
    };
  }

  return next;
}

/** True when a day has something worth copying - open, with at least one interval. */
export function canCopyDay(week: WeeklyHours, source: DayOfWeekNumber): boolean {
  return week[source].isEnabled && week[source].intervals.length > 0;
}
