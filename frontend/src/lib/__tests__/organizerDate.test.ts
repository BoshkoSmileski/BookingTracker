import { describe, expect, it } from 'vitest';
import { organizerDateTime, organizerDayOfWeek, organizerToday } from '../calendarDates';

/**
 * "Today", on the organizer's clock rather than the browser's.
 *
 * Every organizer-side comparison runs against `selectedDate`/`selectedTime`,
 * which are organizer wall-clock columns - so reading "now" from the browser
 * was wrong for any organizer working from a different zone than the one their
 * working schedule is configured in, and wrong for everyone around midnight.
 *
 * Each case is pinned at a **real UTC instant either side of a date boundary**,
 * where the answer differs by a whole day between zones. That is the only way
 * to test this: at midday UTC nearly every zone agrees, so a test that did not
 * pick a boundary would pass against the old browser-clock code too.
 */
describe('organizer-local calendar date', () => {
  it('is the previous day west of the line at a UTC instant just after midnight', () => {
    // 2026-08-14T01:30Z. In New York (UTC-4 in August) it is still the 13th.
    const at = new Date('2026-08-14T01:30:00Z');

    expect(organizerDateTime('UTC', at).date).toBe('2026-08-14');
    expect(organizerDateTime('America/New_York', at).date).toBe('2026-08-13');
  });

  it('is the next day east of the line at a UTC instant just before midnight', () => {
    // 2026-08-13T23:30Z. In Tokyo (UTC+9) it is already the 14th.
    const at = new Date('2026-08-13T23:30:00Z');

    expect(organizerDateTime('UTC', at).date).toBe('2026-08-13');
    expect(organizerDateTime('Asia/Tokyo', at).date).toBe('2026-08-14');
  });

  it('reports the time on that clock, not the UTC one', () => {
    const at = new Date('2026-08-14T01:30:00Z');

    expect(organizerDateTime('Europe/Skopje', at).time).toBe('03:30:00');
    expect(organizerDateTime('America/New_York', at).time).toBe('21:30:00');
  });

  it('reports midnight as 00, never 24', () => {
    // Some engines format hour 0 as "24" under hour12:false; a "24:00:00" would
    // sort after every real time and make a booking at midnight look past.
    const at = new Date('2026-08-14T00:00:00Z');
    expect(organizerDateTime('UTC', at).time).toBe('00:00:00');
  });

  it('derives the weekday from the organizer date, so the two cannot disagree', () => {
    // 2026-08-13 is a Thursday (4); 2026-08-14 a Friday (5).
    expect(organizerDayOfWeek('Asia/Tokyo')).toBe(
      new Date(`${organizerToday('Asia/Tokyo')}T00:00:00`).getDay(),
    );
    expect(organizerDayOfWeek('America/New_York')).toBe(
      new Date(`${organizerToday('America/New_York')}T00:00:00`).getDay(),
    );
  });

  it('falls back to the browser rather than throwing on an unusable zone', () => {
    // An organizer with no working schedule, or a zone the platform does not
    // know: the dashboard must still render, exactly as it did before.
    const at = new Date('2026-08-14T12:00:00Z');
    for (const zone of [null, undefined, '', 'Not/AZone']) {
      expect(organizerDateTime(zone, at).date).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    }
  });

  it('formats as the same yyyy-MM-dd shape selectedDate uses, so they compare directly', () => {
    expect(organizerToday('Europe/Skopje')).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });
});
