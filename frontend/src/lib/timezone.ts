/**
 * Naming a time zone, and nothing else.
 *
 * This file deliberately performs **no scheduling arithmetic**. Every time a
 * visitor sees is already resolved by the backend: `AvailableSlotDto` carries
 * both the organizer's wall clock (`localStartTime`) and the real instant
 * (`startUtc`), computed once by `SlotGenerationService` against the
 * organizer's `TimeZoneInfo`. The frontend's job is to *label* which of those
 * two it is showing - not to work out a second answer, which is exactly how a
 * booking app ends up with two clocks that disagree.
 *
 * That disagreement was real here before this file existed: the time step
 * labelled its buttons in the visitor's browser zone (converted from
 * `startUtc`) while every screen after it printed the organizer's wall clock,
 * so a guest could pick "04:00" and be told on the next screen they had booked
 * "10:00". Both readings were correct; neither said whose clock it was on.
 *
 * The rule this establishes: the wizard shows **organizer-local** time
 * throughout, because that is what gets stored, emailed and put in the `.ics`.
 * The visitor's own zone appears only as a secondary reading beside a slot they
 * have actually chosen, and only when the two differ.
 */

/** The zone the visitor's browser is in, e.g. "Europe/London". */
export function viewerTimeZone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone;
}

/**
 * Whether a booking's zone is also the visitor's, so a screen can skip the
 * "…your time" second reading rather than printing the same clock twice.
 *
 * Compared by IANA id rather than by current offset: "Europe/London" and
 * "Africa/Abidjan" share an offset for part of the year and are not the same
 * zone, and telling a Londoner in January that 10:00 is "10:00 your time"
 * reads as a bug even though it is true today.
 */
export function isViewerTimeZone(timeZoneId: string): boolean {
  return timeZoneId === viewerTimeZone();
}

/**
 * The GMT offset of a zone, as a short label: "GMT+2", "GMT-4:30", "GMT".
 *
 * Read out of `Intl`'s own `shortOffset` output rather than computed, so DST is
 * whatever the platform's tz database says it is on the given date - the offset
 * of a zone is not a constant, and hardcoding one is how a booking page reads
 * "GMT+1" all summer.
 */
export function timeZoneOffsetLabel(timeZoneId: string, at: Date = new Date()): string | null {
  try {
    const parts = new Intl.DateTimeFormat('en-US', {
      timeZone: timeZoneId,
      timeZoneName: 'shortOffset',
    }).formatToParts(at);
    return parts.find((p) => p.type === 'timeZoneName')?.value ?? null;
  } catch {
    // An id the platform's tz database does not know. The caller still has the
    // id itself to show, which is more useful than nothing.
    return null;
  }
}

/**
 * How a time zone is named to a visitor: "Europe/Skopje (GMT+2)".
 *
 * The IANA id is kept rather than replaced by a friendly city name, because it
 * is the unambiguous part - "Central European Time" is two different offsets
 * depending on the month, and a guest checking a booking against their own
 * calendar needs the offset, which is what the parenthetical supplies.
 */
export function timeZoneLabel(timeZoneId: string, at: Date = new Date()): string {
  const offset = timeZoneOffsetLabel(timeZoneId, at);
  return offset ? `${timeZoneId} (${offset})` : timeZoneId;
}

/**
 * A real UTC instant, read on the visitor's own clock: "09:00".
 *
 * Takes `AvailableSlotDto.startUtc`/`endUtc`, which are computed in-memory by
 * the backend with `Kind=Utc` and therefore always serialize with an offset -
 * so this is the documented exception to the `lib/dates.ts` rule rather than a
 * second copy of `parseUtc`.
 */
export function formatUtcOnViewerClock(utcIso: string): string {
  return new Date(utcIso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}
