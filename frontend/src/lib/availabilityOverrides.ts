import { formatCalendarTime } from './calendarDates';
import type { AvailabilityOverrideDto, TimeRangeDto } from './types';

/** Mirrors AvailabilityOverride.MaxRanges. */
export const MAX_OVERRIDE_RANGES = 6;

/** One editable row in the hours editor. `HH:mm`, which is what `<input type="time">` reads and writes. */
export interface RangeInput {
  start: string;
  end: string;
}

/**
 * Backend `HH:mm:ss` ranges into the `HH:mm` pairs the time inputs use.
 *
 * These are wall-clock times, never UTC instants, so they are trimmed here
 * rather than parsed through `lib/dates.ts` - the same distinction
 * `calendarDates.ts` exists to keep obvious.
 */
export function toRangeInputs(ranges: TimeRangeDto[]): RangeInput[] {
  return ranges.map((r) => ({ start: r.start.slice(0, 5), end: r.end.slice(0, 5) }));
}

/**
 * How one override's hours read in the list: "Closed", or the ranges joined.
 *
 * A single helper rather than the branch inlined at each call site, so the list
 * row and any future summary cannot disagree about what an empty `ranges`
 * means - the one place that decision is made on the frontend.
 */
export function describeOverrideHours(override: AvailabilityOverrideDto): string {
  if (override.isClosed) return 'Closed';
  return override.ranges
    .map((r) => `${formatCalendarTime(r.start)}–${formatCalendarTime(r.end)}`)
    .join(', ');
}
