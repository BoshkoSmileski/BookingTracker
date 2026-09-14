import { formatCalendarDate } from './calendarDates';
import { describeOverrideHours } from './availabilityOverrides';
import type { AvailabilityExceptionDto, AvailabilityOverrideDto } from './types';

/**
 * The two ways a date can depart from the weekly working hours, as one list.
 *
 * The backend keeps these genuinely separate and must continue to:
 * `AvailabilityException` **removes** time and `AvailabilityOverride`
 * **produces** it, which is the distinction the domain model exists to
 * protect. What they share is the organizer's question - "what is different
 * about this date?" - and that question is answered in one place, so the
 * merging happens here, at the presentation edge, and nowhere deeper.
 *
 * Everything in this file is a pure function of what the two endpoints already
 * return. No availability is computed here; `SlotGenerationService` remains the
 * only thing that decides what is bookable.
 */

/** A blocked day or period. Subtractive - it removes time from whatever the day was open. */
export interface BlockEntry {
  kind: 'block';
  /** Unique across both kinds, so one list can key on it. */
  key: string;
  /** Inclusive first day, "yyyy-MM-dd". What the list sorts on. */
  date: string;
  exception: AvailabilityExceptionDto;
}

/** Date-specific hours. Replaces the weekly schedule on its date. */
export interface HoursEntry {
  kind: 'hours';
  key: string;
  date: string;
  override: AvailabilityOverrideDto;
}

export type DateExceptionEntry = BlockEntry | HoursEntry;

/**
 * Both lists, ordered by the date they start on.
 *
 * A block and an override on the same date are listed block-first, matching the
 * order they actually apply in: an override says when the day is open, and a
 * block then subtracts from it.
 */
export function mergeDateExceptions(
  exceptions: AvailabilityExceptionDto[],
  overrides: AvailabilityOverrideDto[],
): DateExceptionEntry[] {
  const entries: DateExceptionEntry[] = [
    ...exceptions.map((exception): BlockEntry => ({
      kind: 'block',
      key: `block-${exception.id}`,
      date: exception.date,
      exception,
    })),
    ...overrides.map((override): HoursEntry => ({
      kind: 'hours',
      key: `hours-${override.id}`,
      date: override.date,
      override,
    })),
  ];

  // "yyyy-MM-dd" sorts correctly as a string, which is the whole reason the
  // backend sends calendar dates in that shape - no Date construction, and so
  // no timezone to get wrong.
  return entries.sort((a, b) => {
    if (a.date !== b.date) return a.date < b.date ? -1 : 1;
    if (a.kind === b.kind) return 0;
    return a.kind === 'block' ? -1 : 1;
  });
}

/** One day reads as a date; a period reads as a span with its length, which is the number an organizer checks. */
export function formatPeriod(exception: AvailabilityExceptionDto): string {
  if (exception.totalDays <= 1) return formatCalendarDate(exception.date);
  return `${formatCalendarDate(exception.date)} – ${formatCalendarDate(exception.endDate)} (${exception.totalDays} days)`;
}

/** The date, or span of dates, an entry is about - whichever kind it is. */
export function entryPeriod(entry: DateExceptionEntry): string {
  return entry.kind === 'block' ? formatPeriod(entry.exception) : formatCalendarDate(entry.override.date);
}

/** What the entry does to that date, in the organizer's words rather than the domain's. */
export function entrySummary(entry: DateExceptionEntry): string {
  if (entry.kind === 'hours') {
    return entry.override.isClosed ? 'Closed all day' : describeOverrideHours(entry.override);
  }
  const { startTime, endTime } = entry.exception;
  if (!startTime || !endTime) return 'Unavailable all day';
  return `Unavailable ${startTime.slice(0, 5)}–${endTime.slice(0, 5)}`;
}

/**
 * True when a whole-day block covers this override's date, so the hours it sets
 * cannot produce a single slot.
 *
 * A display hint, not a second implementation of the precedence rule: it warns
 * about the one case that is unambiguous from the two lists alone. Whether any
 * given slot survives is still decided entirely by `SlotGenerationService`,
 * which also subtracts bookings, buffers, notice periods and busy calendar
 * time this cannot see.
 */
export function isBlockedByWholeDay(
  override: AvailabilityOverrideDto,
  exceptions: AvailabilityExceptionDto[],
): boolean {
  return exceptions.some(
    (e) => e.startTime === null && e.date <= override.date && override.date <= e.endDate,
  );
}
