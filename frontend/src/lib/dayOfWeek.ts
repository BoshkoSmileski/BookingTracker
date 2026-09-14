import type { DayOfWeekNumber } from './types';

export const DAY_NAMES: Record<DayOfWeekNumber, string> = {
  0: 'Sunday',
  1: 'Monday',
  2: 'Tuesday',
  3: 'Wednesday',
  4: 'Thursday',
  5: 'Friday',
  6: 'Saturday',
};

/** Monday-first display order, since that's how the working-hours editor and dashboard present the week. */
export const DAY_DISPLAY_ORDER: DayOfWeekNumber[] = [1, 2, 3, 4, 5, 6, 0];
