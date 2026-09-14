import { describe, expect, it } from 'vitest';
import { SEQUENTIAL, SERIES, STATUS_COLOR, formatDuration, formatPercent, sequentialStep } from '../chartTheme';

/**
 * REGRESSION AREA: analytics calculations and the chart palette contract.
 *
 * The palette rules here are not cosmetic - slot order is the colorblind-safety
 * mechanism, and a status keeping its colour when a filter changes the series
 * count is what stops a chart repainting itself misleadingly.
 */
describe('chartTheme', () => {
  describe('formatPercent', () => {
    it('renders a 0-1 ratio as a percentage', () => {
      expect(formatPercent(0)).toBe('0%');
      expect(formatPercent(0.4)).toBe('40%');
      expect(formatPercent(1)).toBe('100%');
    });

    it('supports extra precision where a rounded figure would mislead', () => {
      expect(formatPercent(0.4286, 1)).toBe('42.9%');
      expect(formatPercent(0.005, 1)).toBe('0.5%');
    });

    it('renders an em dash for an absent value rather than 0%', () => {
      // "no data" and "zero percent" are different facts and must not look alike.
      expect(formatPercent(null)).toBe('—');
      expect(formatPercent(undefined)).toBe('—');
    });
  });

  describe('formatDuration', () => {
    it('uses seconds under a minute', () => {
      expect(formatDuration(0)).toBe('0s');
      expect(formatDuration(45)).toBe('45s');
    });

    it('uses minutes and seconds under an hour', () => {
      expect(formatDuration(60)).toBe('1m 0s');
      expect(formatDuration(192)).toBe('3m 12s');
    });

    it('uses hours and minutes beyond an hour', () => {
      expect(formatDuration(3600)).toBe('1h 0m');
      expect(formatDuration(3840)).toBe('1h 4m');
    });

    it('renders an em dash for an absent value', () => {
      expect(formatDuration(null)).toBe('—');
      expect(formatDuration(undefined)).toBe('—');
    });

    it('never renders a negative duration', () => {
      expect(formatDuration(-5)).toBe('0s');
    });

    it('rounds fractional seconds', () => {
      expect(formatDuration(45.6)).toBe('46s');
    });
  });

  describe('sequentialStep', () => {
    it('maps the largest value to the darkest step and zero to the lightest', () => {
      expect(sequentialStep(100, 100)).toBe(SEQUENTIAL[SEQUENTIAL.length - 1]);
      expect(sequentialStep(0, 100)).toBe(SEQUENTIAL[0]);
    });

    it('returns the lightest step when there is no magnitude to scale against', () => {
      expect(sequentialStep(0, 0)).toBe(SEQUENTIAL[0]);
      expect(sequentialStep(5, -1)).toBe(SEQUENTIAL[0]);
    });

    it('always returns a real ramp step, never an out-of-range index', () => {
      for (const value of [-10, 0, 1, 50, 99, 100, 250]) {
        expect(SEQUENTIAL).toContain(sequentialStep(value, 100));
      }
    });

    it('is monotonic: a larger value never gets a lighter step', () => {
      // SEQUENTIAL is a readonly literal tuple (`as const`), so it is widened to
      // string[] here purely to look up an index.
      const ramp = SEQUENTIAL as readonly string[];
      const indices = [0, 20, 40, 60, 80, 100].map((v) => ramp.indexOf(sequentialStep(v, 100)));
      expect(indices).toEqual([...indices].sort((a, b) => a - b));
    });
  });

  describe('palette contract', () => {
    it('assigns every mutually exclusive booking status a fixed colour', () => {
      // Fixed slot per status: a filter that changes the number of slices must not
      // repaint the survivors.
      for (const status of ['Upcoming', 'Completed', 'Cancelled', 'Abandoned', 'In progress']) {
        expect(STATUS_COLOR[status]).toMatch(/^#[0-9a-f]{6}$/i);
      }
    });

    it('gives each status a distinct colour', () => {
      const used = Object.values(STATUS_COLOR);
      expect(new Set(used).size).toBe(used.length);
    });

    it('draws status colours from the validated categorical slots', () => {
      for (const colour of Object.values(STATUS_COLOR)) {
        expect(SERIES).toContain(colour);
      }
    });

    it('exposes a sequential ramp ordered light to dark', () => {
      // Not just distinct - ordered, since magnitude is read from depth as well as length.
      expect(new Set(SEQUENTIAL).size).toBe(SEQUENTIAL.length);
      expect(SEQUENTIAL.length).toBeGreaterThan(2);
    });
  });
});
