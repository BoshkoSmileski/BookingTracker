import { describe, expect, it } from 'vitest';
import {
  entryPeriod, entrySummary, formatPeriod, isBlockedByWholeDay, mergeDateExceptions,
} from '../dateExceptions';
import { availabilityException, availabilityOverride } from '../../test/factories';

describe('mergeDateExceptions', () => {
  it('interleaves both kinds in date order, so one month reads as one list', () => {
    // The point of consolidating the two screens: "22nd I work mornings, 25th-30th
    // I am away" has to be readable in sequence, not as two lists to cross-check.
    const entries = mergeDateExceptions(
      [availabilityException({ id: 'b1', date: '2026-08-25', endDate: '2026-08-30', totalDays: 6 })],
      [
        availabilityOverride({ id: 'h2', date: '2026-09-01' }),
        availabilityOverride({ id: 'h1', date: '2026-08-22' }),
      ],
    );

    expect(entries.map((e) => [e.kind, e.date])).toEqual([
      ['hours', '2026-08-22'],
      ['block', '2026-08-25'],
      ['hours', '2026-09-01'],
    ]);
  });

  it('lists a block before hours on the same date, matching the order they apply', () => {
    // An override says when the day is OPEN; a block then subtracts from it, and
    // a whole-day block wins outright.
    const entries = mergeDateExceptions(
      [availabilityException({ date: '2026-08-22' })],
      [availabilityOverride({ date: '2026-08-22' })],
    );

    expect(entries.map((e) => e.kind)).toEqual(['block', 'hours']);
  });

  it('keys the two kinds apart, so ids from different tables cannot collide', () => {
    const entries = mergeDateExceptions(
      [availabilityException({ id: 'same' })],
      [availabilityOverride({ id: 'same' })],
    );

    expect(new Set(entries.map((e) => e.key)).size).toBe(2);
  });

  it('is empty when the organizer has neither', () => {
    expect(mergeDateExceptions([], [])).toEqual([]);
  });
});

describe('formatPeriod', () => {
  it('reads a single day as a date', () => {
    const label = formatPeriod(availabilityException({ date: '2026-08-25', endDate: '2026-08-25', totalDays: 1 }));

    // Derived from the same formatter the code uses: a hardcoded English month
    // fails on any machine with another locale.
    const expected = new Date(2026, 7, 25).toLocaleDateString(undefined, {
      day: 'numeric', month: 'short', year: 'numeric',
    });
    expect(label).toBe(expected);
  });

  it('reads a period as a span carrying its own length', () => {
    // The count comes from the backend's `totalDays`, never re-derived here, so
    // the two can never disagree about an inclusive boundary.
    const label = formatPeriod(
      availabilityException({ date: '2026-08-25', endDate: '2026-08-30', totalDays: 6 }),
    );

    expect(label).toContain('–');
    expect(label).toContain('(6 days)');
  });
});

describe('entrySummary', () => {
  it('says a whole-day block is unavailable', () => {
    const entry = mergeDateExceptions([availabilityException()], [])[0];
    expect(entrySummary(entry)).toBe('Unavailable all day');
  });

  it('names the window a timed block removes', () => {
    const entry = mergeDateExceptions(
      [availabilityException({ startTime: '12:00:00', endTime: '13:00:00' })],
      [],
    )[0];
    expect(entrySummary(entry)).toBe('Unavailable 12:00–13:00');
  });

  it('lists the hours an override opens', () => {
    const entry = mergeDateExceptions(
      [],
      [availabilityOverride({ ranges: [{ start: '09:00:00', end: '12:00:00' }, { start: '13:00:00', end: '17:00:00' }] })],
    )[0];
    expect(entrySummary(entry)).toBe('09:00–12:00, 13:00–17:00');
  });

  it('says a closed override is closed rather than showing no hours at all', () => {
    const entry = mergeDateExceptions([], [availabilityOverride({ isClosed: true, ranges: [] })])[0];
    expect(entrySummary(entry)).toBe('Closed all day');
  });
});

describe('entryPeriod', () => {
  it('gives an override its single date and a block its span', () => {
    const [block, hours] = mergeDateExceptions(
      [availabilityException({ date: '2026-08-01', endDate: '2026-08-03', totalDays: 3 })],
      [availabilityOverride({ date: '2026-08-22' })],
    );

    expect(entryPeriod(block)).toContain('(3 days)');
    expect(entryPeriod(hours)).not.toContain('days)');
  });
});

describe('isBlockedByWholeDay', () => {
  const override = availabilityOverride({ date: '2026-08-27' });

  it('sees a whole-day block whose range reaches the date', () => {
    // Inclusive at both ends, like AvailabilityException.Covers.
    expect(isBlockedByWholeDay(override, [
      availabilityException({ date: '2026-08-25', endDate: '2026-08-30', totalDays: 6 }),
    ])).toBe(true);
  });

  it('sees a single-day block on exactly that date', () => {
    expect(isBlockedByWholeDay(override, [availabilityException({ date: '2026-08-27', endDate: '2026-08-27' })])).toBe(true);
  });

  it('does not claim a date outside the block', () => {
    expect(isBlockedByWholeDay(override, [
      availabilityException({ date: '2026-08-28', endDate: '2026-08-30', totalDays: 3 }),
    ])).toBe(false);
  });

  it('stays quiet about a timed block, which only narrows the day', () => {
    // A partial-day block subtracts an hour; whether any slot survives is
    // SlotGenerationService's answer, not this hint's.
    expect(isBlockedByWholeDay(override, [
      availabilityException({ date: '2026-08-27', endDate: '2026-08-27', startTime: '12:00:00', endTime: '13:00:00' }),
    ])).toBe(false);
  });

  it('is false when nothing is blocked at all', () => {
    expect(isBlockedByWholeDay(override, [])).toBe(false);
  });
});
