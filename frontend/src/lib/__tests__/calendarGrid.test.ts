import { describe, expect, it } from 'vitest';
import { buildMonthGrid, endOfMonthKey, startOfMonthKey, toDateKey } from '../calendarGrid';

describe('calendarGrid', () => {
  describe('toDateKey', () => {
    it('formats as yyyy-MM-dd with zero padding', () => {
      expect(toDateKey(new Date(2026, 0, 5))).toBe('2026-01-05');
      expect(toDateKey(new Date(2026, 11, 31))).toBe('2026-12-31');
    });

    it('uses local date parts, matching how the date picker is rendered', () => {
      // Deliberately local: these keys index slots by the organizer's calendar day,
      // not by a UTC instant.
      const d = new Date(2026, 7, 4, 23, 30);
      expect(toDateKey(d)).toBe('2026-08-04');
    });
  });

  describe('buildMonthGrid', () => {
    it('always returns a 6x7 grid', () => {
      expect(buildMonthGrid(new Date(2026, 7, 1))).toHaveLength(42);
      expect(buildMonthGrid(new Date(2026, 1, 1))).toHaveLength(42);
    });

    it('starts on the Monday on or before the first of the month', () => {
      // 1 Aug 2026 is a Saturday, so the grid opens on Monday 27 July.
      const grid = buildMonthGrid(new Date(2026, 7, 1));

      expect(grid[0].getDay()).toBe(1);
      expect(toDateKey(grid[0])).toBe('2026-07-27');
    });

    it('starts on the first itself when the month begins on a Monday', () => {
      // 1 June 2026 is a Monday - no leading days from May.
      const grid = buildMonthGrid(new Date(2026, 5, 1));

      expect(toDateKey(grid[0])).toBe('2026-06-01');
    });

    it('handles a Sunday start by showing a full leading week', () => {
      // 1 Nov 2026 is a Sunday; Monday-first means six leading days from October.
      const grid = buildMonthGrid(new Date(2026, 10, 1));

      expect(toDateKey(grid[0])).toBe('2026-10-26');
      expect(toDateKey(grid[6])).toBe('2026-11-01');
    });

    it('produces consecutive days with no gaps or repeats', () => {
      const grid = buildMonthGrid(new Date(2026, 7, 1));

      for (let i = 1; i < grid.length; i++) {
        const gapDays = Math.round((grid[i].getTime() - grid[i - 1].getTime()) / 86_400_000);
        expect(gapDays).toBe(1);
      }
    });

    it('spans a month boundary across a DST change without losing a day', () => {
      // October in Europe contains a DST rollback; naive +24h arithmetic would
      // repeat or skip a day here.
      const grid = buildMonthGrid(new Date(2026, 9, 1));
      const keys = grid.map(toDateKey);

      expect(new Set(keys).size).toBe(42);
    });

    it('contains every day of the target month', () => {
      const grid = buildMonthGrid(new Date(2026, 1, 1)); // February 2026, 28 days
      const februaryDays = grid.filter((d) => d.getMonth() === 1);

      expect(februaryDays).toHaveLength(28);
    });
  });

  describe('month boundary keys', () => {
    it('returns the first and last day of the cursor month', () => {
      const cursor = new Date(2026, 7, 15);

      expect(startOfMonthKey(cursor)).toBe('2026-08-01');
      expect(endOfMonthKey(cursor)).toBe('2026-08-31');
    });

    it('handles February in a non-leap year', () => {
      expect(endOfMonthKey(new Date(2026, 1, 10))).toBe('2026-02-28');
    });

    it('handles February in a leap year', () => {
      expect(endOfMonthKey(new Date(2028, 1, 10))).toBe('2028-02-29');
    });

    it('handles December without rolling into the next year', () => {
      const cursor = new Date(2026, 11, 15);

      expect(startOfMonthKey(cursor)).toBe('2026-12-01');
      expect(endOfMonthKey(cursor)).toBe('2026-12-31');
    });
  });
});
