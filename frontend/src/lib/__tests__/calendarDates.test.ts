import { describe, expect, it } from 'vitest';
import {
  addMinutesToCalendarTime,
  formatCalendarDate,
  formatCalendarDateLong,
  formatCalendarDateTime,
  formatCalendarTime,
  formatCalendarTimeRange,
} from '../calendarDates';

/**
 * Wall-clock calendar values - `selectedDate`, `selectedTime`, an
 * `AvailabilityException` date. Deliberately NOT parsed as instants: `new
 * Date("2026-08-20")` is midnight UTC, which is the previous day in every
 * negative-offset zone, and these values are not instants at all.
 */
describe('formatCalendarDate', () => {
  it('builds the date from its parts, so no timezone is ever applied', () => {
    // Compared against a locally-constructed Date rather than a literal string,
    // per the rule against hardcoding locale-dependent output.
    expect(formatCalendarDate('2026-08-04')).toBe(
      new Date(2026, 7, 4).toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' }),
    );
  });

  it('does not shift the day, which parsing it as an instant would', () => {
    // The bug this file exists to prevent: whatever the machine's zone, the 1st
    // must format as the 1st.
    expect(formatCalendarDate('2026-01-01')).toContain('1');
    expect(formatCalendarDate('2026-01-01')).toContain('2026');
    expect(formatCalendarDate('2026-01-01')).not.toContain('2025');
  });
});

describe('formatCalendarTime', () => {
  it('drops the always-zero seconds', () => {
    expect(formatCalendarTime('14:30:00')).toBe('14:30');
  });
});

describe('formatCalendarDateTime', () => {
  it('joins the two', () => {
    expect(formatCalendarDateTime('2026-08-04', '14:30:00')).toBe(
      `${formatCalendarDate('2026-08-04')} at 14:30`,
    );
  });
});

describe('formatCalendarDateLong', () => {
  it('leads with the weekday, which is what people check a booking against', () => {
    expect(formatCalendarDateLong('2026-08-20')).toBe(
      new Date(2026, 7, 20).toLocaleDateString(undefined, {
        weekday: 'long', day: 'numeric', month: 'long', year: 'numeric',
      }),
    );
  });
});

describe('formatCalendarTimeRange', () => {
  it('renders a range with an en dash', () => {
    expect(formatCalendarTimeRange('09:00:00', '09:30:00')).toBe('09:00 – 09:30');
  });
});

describe('addMinutesToCalendarTime', () => {
  it('adds within the hour', () => {
    expect(addMinutesToCalendarTime('09:00:00', 30)).toBe('09:30');
  });

  it('carries across an hour boundary', () => {
    expect(addMinutesToCalendarTime('09:45:00', 30)).toBe('10:15');
  });

  it('carries across several hours', () => {
    expect(addMinutesToCalendarTime('09:00', 150)).toBe('11:30');
  });

  it('accepts a value with or without seconds, since both shapes reach it', () => {
    // The API sends "09:00:00"; the tracker's form state holds "09:00".
    expect(addMinutesToCalendarTime('09:00', 30)).toBe(addMinutesToCalendarTime('09:00:00', 30));
  });

  it('clamps at midnight rather than wrapping to an earlier-looking time', () => {
    // No booking page offers a slot that runs past midnight; if one somehow
    // did, "00:30" would read as a booking that ends before it starts.
    expect(addMinutesToCalendarTime('23:45:00', 30)).toBe('24:00');
  });
});
