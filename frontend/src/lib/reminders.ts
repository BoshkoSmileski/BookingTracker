/**
 * Reminder lead-time presets and formatting. Mirrors the backend's
 * ReminderWindow.Label wording so the checkbox grid, the live preview, and the
 * reminder list on a booking all say "24 hours" for 1440 - the same string the
 * guest sees in the email subject.
 */

/** Offered as checkboxes in Notification Settings. Backend allows any value in range; these are just the sensible ones. */
export const REMINDER_PRESETS: { minutes: number; label: string }[] = [
  { minutes: 15, label: '15 minutes' },
  { minutes: 30, label: '30 minutes' },
  { minutes: 60, label: '1 hour' },
  { minutes: 120, label: '2 hours' },
  { minutes: 360, label: '6 hours' },
  { minutes: 720, label: '12 hours' },
  { minutes: 1440, label: '24 hours' },
  { minutes: 2880, label: '2 days' },
];

/** Matches Domain NotificationSettings.MaxReminderIntervals - saving more than this is rejected server-side. */
export const MAX_REMINDER_INTERVALS = 8;

function plural(value: number, unit: string): string {
  return `${value} ${unit}${value === 1 ? '' : 's'}`;
}

/**
 * Same rules as the backend's ReminderWindow.Label, so a custom (non-preset)
 * value still reads correctly.
 *
 * 1440 is special-cased to "24 hours" to match the backend exactly - without it
 * the preset grid (which labels it "24 hours") and this function disagreed about
 * the default reminder, showing "24 hours before" and "1 day before" for the same
 * checkbox on the same screen, and neither matched the email subject.
 */
export function formatReminderLabel(minutes: number): string {
  if (minutes === 1440) return '24 hours';
  if (minutes % 1440 === 0) return plural(minutes / 1440, 'day');
  if (minutes % 60 === 0) return plural(minutes / 60, 'hour');
  return plural(minutes, 'minute');
}

/**
 * Worked example for the settings preview: when each configured reminder would
 * land for a meeting tomorrow at 10:00 local time. Concrete times make an
 * abstract list of offsets immediately understandable ("6 hours before" is
 * 04:00 - probably not what you wanted).
 */
export function buildReminderPreview(minutesList: number[]): { label: string; when: string }[] {
  const meeting = new Date();
  meeting.setDate(meeting.getDate() + 1);
  meeting.setHours(10, 0, 0, 0);

  return [...minutesList]
    .sort((a, b) => b - a)
    .map((minutes) => {
      const fireAt = new Date(meeting.getTime() - minutes * 60_000);
      const sameDayAsMeeting = fireAt.getDate() === meeting.getDate();
      const clock = fireAt.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
      const dayLabel = sameDayAsMeeting ? 'Tomorrow' : fireAt.toLocaleDateString([], { weekday: 'long' });
      return { label: `${formatReminderLabel(minutes)} before`, when: `${dayLabel} at ${clock}` };
    });
}

export const REMINDER_PREVIEW_CAPTION =
  'Example based on a meeting tomorrow at 10:00, shown in your browser’s local time.';
