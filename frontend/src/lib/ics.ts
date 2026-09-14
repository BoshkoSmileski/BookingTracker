interface IcsEventInput {
  uid: string;
  title: string;
  description?: string;
  startUtc: string;
  endUtc: string;
}

function toIcsUtc(iso: string): string {
  // "2026-08-05T07:00:00Z" -> "20260805T070000Z"
  return iso.replace(/[-:]/g, '').replace(/\.\d+Z$/, 'Z');
}

function escapeIcsText(text: string): string {
  return text.replace(/\\/g, '\\\\').replace(/,/g, '\\,').replace(/;/g, '\\;').replace(/\n/g, '\\n');
}

/**
 * Builds a downloadable .ics file client-side - no backend calendar
 * integration exists, but a real, importable calendar file is easy to
 * generate from data the frontend already has, so "Add to Calendar" doesn't
 * have to be a fake disabled button.
 */
export function downloadIcsFile(event: IcsEventInput): void {
  const lines = [
    'BEGIN:VCALENDAR',
    'VERSION:2.0',
    'PRODID:-//BookingTracker//Booking//EN',
    'BEGIN:VEVENT',
    `UID:${event.uid}`,
    `DTSTAMP:${toIcsUtc(new Date().toISOString())}`,
    `DTSTART:${toIcsUtc(event.startUtc)}`,
    `DTEND:${toIcsUtc(event.endUtc)}`,
    `SUMMARY:${escapeIcsText(event.title)}`,
    ...(event.description ? [`DESCRIPTION:${escapeIcsText(event.description)}`] : []),
    'END:VEVENT',
    'END:VCALENDAR',
  ];

  const blob = new Blob([lines.join('\r\n')], { type: 'text/calendar;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = 'appointment.ics';
  link.click();
  URL.revokeObjectURL(url);
}
