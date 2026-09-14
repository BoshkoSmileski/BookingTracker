import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  MAX_REMINDER_INTERVALS, REMINDER_PRESETS, buildReminderPreview, formatReminderLabel,
} from '../reminders';

/**
 * REGRESSION AREA: reminder preview generation. The labels here deliberately
 * mirror the backend's ReminderWindow.Label, so the settings preview and the
 * email subject agree - a drift between them is a real, user-visible bug.
 */
describe('reminders', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  describe('formatReminderLabel', () => {
    it.each([
      [15, '15 minutes'],
      [30, '30 minutes'],
      [60, '1 hour'],
      [120, '2 hours'],
      [720, '12 hours'],
      [1440, '24 hours'],
      [2880, '2 days'],
    ])('formats %i minutes as "%s"', (minutes, expected) => {
      expect(formatReminderLabel(minutes)).toBe(expected);
    });

    it('singularises exactly one unit', () => {
      expect(formatReminderLabel(60)).toBe('1 hour');
      expect(formatReminderLabel(2880)).toBe('2 days');
      expect(formatReminderLabel(1)).toBe('1 minute');
    });

    it('matches the backend wording for the default 24-hour reminder', () => {
      // ReminderWindow.Label(1440) is "24 hours" server-side; a "1 day" here would
      // disagree with both the preset grid and the email subject.
      expect(formatReminderLabel(1440)).toBe('24 hours');
    });

    it('falls back to minutes for a value that is not a whole hour or day', () => {
      expect(formatReminderLabel(90)).toBe('90 minutes');
      expect(formatReminderLabel(45)).toBe('45 minutes');
    });
  });

  describe('presets', () => {
    it('offers no more presets than the backend will accept', () => {
      // Saving more than MaxReminderIntervals is rejected server-side, so the grid
      // must never present a selection that cannot be saved.
      expect(REMINDER_PRESETS.length).toBeLessThanOrEqual(MAX_REMINDER_INTERVALS);
    });

    it('is ordered shortest-first and free of duplicates', () => {
      const minutes = REMINDER_PRESETS.map((p) => p.minutes);
      expect(minutes).toEqual([...minutes].sort((a, b) => a - b));
      expect(new Set(minutes).size).toBe(minutes.length);
    });

    it('labels each preset the same way formatReminderLabel would', () => {
      for (const preset of REMINDER_PRESETS) {
        expect(preset.label).toBe(formatReminderLabel(preset.minutes));
      }
    });
  });

  describe('buildReminderPreview', () => {
    it('lists the longest lead time first, so the preview reads chronologically', () => {
      const rows = buildReminderPreview([60, 1440, 15]);

      expect(rows.map((r) => r.label)).toEqual(['24 hours before', '1 hour before', '15 minutes before']);
    });

    it('computes concrete fire times against a meeting tomorrow at 10:00', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date(2026, 7, 4, 12, 0, 0)); // local time - the preview is local by design

      const rows = buildReminderPreview([1440, 60]);

      // 24h before a 10:00 meeting tomorrow = 10:00 today.
      expect(rows[0].when).toContain('10:00');
      // 1h before = 09:00 tomorrow.
      expect(rows[1].when).toContain('09:00');
      expect(rows[1].when).toContain('Tomorrow');
    });

    it('labels a reminder that lands the day before with that weekday, not "Tomorrow"', () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date(2026, 7, 4, 12, 0, 0));

      const [row] = buildReminderPreview([1440]);
      // Locale-independent: the weekday is rendered with toLocaleDateString, so the
      // expected name is derived the same way rather than hardcoded in English
      // (the preview is intentionally locale-aware).
      const expectedWeekday = new Date(2026, 7, 4).toLocaleDateString([], { weekday: 'long' });
      const expectedClock = new Date(2026, 7, 4, 10, 0).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });

      expect(row.when).not.toContain('Tomorrow');
      expect(row.when).toBe(`${expectedWeekday} at ${expectedClock}`);
    });

    it('returns nothing when no reminders are selected', () => {
      expect(buildReminderPreview([])).toEqual([]);
    });

    it('does not mutate the caller’s array while sorting', () => {
      const input = [60, 1440, 15];
      buildReminderPreview(input);

      expect(input).toEqual([60, 1440, 15]);
    });
  });
});
