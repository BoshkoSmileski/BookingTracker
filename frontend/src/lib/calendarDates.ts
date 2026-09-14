/**
 * Formatters for **wall-clock** calendar values - `AvailabilityException.date`,
 * `BookingSession.selectedDate`/`selectedTime` - which are plain `DateOnly` /
 * `TimeOnly` values in the organizer's own timezone, never UTC instants.
 *
 * This is deliberately NOT `lib/dates.ts`. That file exists for backend
 * `DateTime` values and appends a `Z` before parsing; doing that here would
 * reinterpret a calendar date as an instant and shift it by a day in
 * negative-offset timezones. Keeping the two in separate files makes it hard to
 * reach for the wrong one by accident.
 */

/** "2026-08-04" -> "4 Aug 2026", built from the parts so no timezone is ever applied. */
export function formatCalendarDate(isoDate: string): string {
  const [year, month, day] = isoDate.split('-').map(Number);
  return new Date(year, month - 1, day).toLocaleDateString(undefined, {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
  });
}

/** "14:30:00" -> "14:30". The seconds are always zero and only add noise. */
export function formatCalendarTime(isoTime: string): string {
  return isoTime.slice(0, 5);
}

/** "2026-08-04" + "14:30:00" -> "4 Aug 2026 at 14:30". */
export function formatCalendarDateTime(isoDate: string, isoTime: string): string {
  return `${formatCalendarDate(isoDate)} at ${formatCalendarTime(isoTime)}`;
}

/**
 * "2026-08-04" -> "Tuesday, 4 August 2026".
 *
 * The long form, for the one or two places a guest is being asked to recognise
 * a specific day rather than scan a list of them - the booking summary and the
 * confirmation. The weekday is the part people actually check a booking
 * against; a bare "4 Aug" is a date you have to look up.
 */
export function formatCalendarDateLong(isoDate: string): string {
  const [year, month, day] = isoDate.split('-').map(Number);
  return new Date(year, month - 1, day).toLocaleDateString(undefined, {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    year: 'numeric',
  });
}

/**
 * "09:00:00" + "09:30:00" -> "09:00 – 09:30".
 *
 * An en dash rather than a hyphen: this is a range, and the hyphen it replaces
 * is what makes "09:00-09:30" read as one token at small sizes.
 */
export function formatCalendarTimeRange(startIsoTime: string, endIsoTime: string): string {
  return `${formatCalendarTime(startIsoTime)} – ${formatCalendarTime(endIsoTime)}`;
}

/**
 * The end of a booking, from its start and duration: ("09:00:00", 30) -> "09:30".
 *
 * Minutes-since-midnight arithmetic, mirroring `SlotGenerationService`'s own
 * choice, so a duration crossing an hour boundary cannot go wrong the way
 * `Date`-based addition can when the wall clock is not a real instant. A
 * booking that would run past midnight clamps at 24:00 rather than wrapping to
 * an earlier-looking time - no booking page offers one, and a wrapped reading
 * would be worse than a clamped one.
 */
export function addMinutesToCalendarTime(isoTime: string, minutes: number): string {
  const [hours, mins] = isoTime.split(':').map(Number);
  const total = Math.min(hours * 60 + mins + minutes, 24 * 60);
  return `${String(Math.floor(total / 60)).padStart(2, '0')}:${String(total % 60).padStart(2, '0')}`;
}

/**
 * The calendar date it is **right now for the organizer**, as the same
 * `yyyy-MM-dd` wall-clock string `selectedDate` uses.
 *
 * This exists because "today" was the browser's, and every organizer-side date
 * comparison ran against it: today's appointments, what is upcoming, which
 * weekday's hours to show, whether a submitted booking is still ahead. Those
 * are all compared to `selectedDate`/`selectedTime`, which are **organizer**
 * wall clock - so an organizer whose browser sits on the other side of a date
 * line from their configured zone saw the wrong day's list, and near midnight
 * every organizer did.
 *
 * **This is not the timezone arithmetic the guest flow forbids.** That
 * rule is about the client reconstructing a booking's *instant* from a wall
 * clock the backend already resolved - inventing a moment nobody sent. This
 * asks the platform's own tz database a question no backend response answers:
 * what today's date is in a named zone. It is the same class of call as
 * `timeZoneLabel`, and it is the only way to compare "now" against a wall-clock
 * column without the browser's zone leaking in.
 *
 * `en-CA` because it formats as `yyyy-MM-dd` - the exact shape these columns
 * use - rather than assembling parts by hand. An unknown zone falls back to the
 * browser, which is the pre-existing behaviour and no worse than it.
 */
export function organizerToday(timeZoneId: string | null | undefined): string {
  return organizerDateTime(timeZoneId).date;
}

/**
 * Today's date **and** the current time on the organizer's clock, as the two
 * wall-clock strings the API uses. One call, because anything comparing a
 * booking to "now" needs both and must not read the clock twice across midnight.
 */
export function organizerDateTime(
  timeZoneId: string | null | undefined,
  at: Date = new Date(),
): { date: string; time: string } {
  try {
    const parts = new Intl.DateTimeFormat('en-CA', {
      timeZone: timeZoneId || undefined,
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: false,
    }).formatToParts(at);

    const get = (type: Intl.DateTimeFormatPartTypes) => parts.find((p) => p.type === type)?.value ?? '';
    // `hour12: false` yields "24" for midnight in some engines; the date part is
    // already correct there, so only the hour needs normalising.
    const hour = get('hour') === '24' ? '00' : get('hour');
    return {
      date: `${get('year')}-${get('month')}-${get('day')}`,
      time: `${hour}:${get('minute')}:${get('second')}`,
    };
  } catch {
    // An unrecognised zone must never take the dashboard down - fall back to the
    // browser, which is exactly what these call sites did before.
    const pad = (n: number) => String(n).padStart(2, '0');
    return {
      date: `${at.getFullYear()}-${pad(at.getMonth() + 1)}-${pad(at.getDate())}`,
      time: `${pad(at.getHours())}:${pad(at.getMinutes())}:${pad(at.getSeconds())}`,
    };
  }
}

/**
 * The organizer's current day of the week, 0=Sunday, matching `WorkingDay.dayOfWeek`.
 * Derived from the same date string above so the two can never disagree.
 */
export function organizerDayOfWeek(timeZoneId: string | null | undefined): number {
  const [year, month, day] = organizerToday(timeZoneId).split('-').map(Number);
  return new Date(year, month - 1, day).getDay();
}
