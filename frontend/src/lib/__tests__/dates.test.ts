import { afterEach, describe, expect, it, vi } from 'vitest';
import { formatClock, formatDateTime, formatRelative } from '../dates';

/**
 * REGRESSION AREA. Backend timestamps round-trip through SQL Server with
 * Kind=Unspecified, so System.Text.Json serializes them WITHOUT a trailing 'Z'
 * even though they are always UTC. A previous bug rendered those as if they
 * were already local time (18:15 UTC displayed as 18:15 instead of 20:15 in
 * UTC+2). These tests pin the offset-less-means-UTC rule.
 *
 * Assertions compare against a Date built from an explicitly-UTC string rather
 * than a hardcoded clock value, so they hold in any machine timezone.
 */
describe('dates', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  describe('formatDateTime', () => {
    it('treats an offset-less backend timestamp as UTC, not local', () => {
      // The exact shape the API returns once a value has been read back from SQL Server.
      const asUtc = new Date('2026-08-04T18:15:00Z');

      expect(formatDateTime('2026-08-04T18:15:00')).toBe(asUtc.toLocaleString());
    });

    it('does not double-apply an offset when one is already present', () => {
      const asUtc = new Date('2026-08-04T18:15:00Z');

      expect(formatDateTime('2026-08-04T18:15:00Z')).toBe(asUtc.toLocaleString());
    });

    it('honours an explicit non-UTC offset', () => {
      // +02:00 means this instant is 16:15Z - it must not be re-interpreted as UTC.
      expect(formatDateTime('2026-08-04T18:15:00+02:00')).toBe(new Date('2026-08-04T16:15:00Z').toLocaleString());
    });

    it('parses fractional seconds, which the backend emits on some fields', () => {
      expect(formatDateTime('2026-08-04T18:15:00.1234567')).toBe(new Date('2026-08-04T18:15:00.123Z').toLocaleString());
    });
  });

  describe('formatClock', () => {
    it('renders 24-hour time from an offset-less UTC timestamp', () => {
      expect(formatClock('2026-08-04T18:15:00')).toBe(
        new Date('2026-08-04T18:15:00Z').toLocaleTimeString([], { hour12: false }),
      );
    });

    it('agrees with formatDateTime about which instant a timestamp is', () => {
      // Both must route through the same parse - a second, drifting copy of the
      // offset logic is exactly what this rule exists to prevent.
      const iso = '2026-08-04T07:05:00';
      expect(formatDateTime(iso)).toContain(formatClock(iso));
    });
  });

  describe('formatRelative', () => {
    it('reports seconds under a minute', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date('2026-08-04T12:00:30Z'));

      expect(formatRelative('2026-08-04T12:00:00')).toBe('30s ago');
    });

    it('reports minutes, hours and days as the gap grows', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date('2026-08-04T12:00:00Z'));

      expect(formatRelative('2026-08-04T11:55:00')).toBe('5m ago');
      expect(formatRelative('2026-08-04T09:00:00')).toBe('3h ago');
      expect(formatRelative('2026-08-01T12:00:00')).toBe('3d ago');
    });

    it('clamps a future timestamp to 0s rather than showing a negative age', () => {
      // Clock skew between server and browser must not render "-4s ago".
      vi.useFakeTimers();
      vi.setSystemTime(new Date('2026-08-04T12:00:00Z'));

      expect(formatRelative('2026-08-04T12:00:05')).toBe('0s ago');
    });

    it('applies the UTC rule, so an offset-less timestamp is not read as local', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date('2026-08-04T12:00:00Z'));

      // If this were parsed as local time in any non-UTC zone the answer would
      // be off by the offset - in UTC+2 it would read "2h ago".
      expect(formatRelative('2026-08-04T12:00:00')).toBe('0s ago');
    });
  });
});
