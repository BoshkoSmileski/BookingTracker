import { describe, expect, it } from 'vitest';
import {
  formatUtcOnViewerClock,
  isViewerTimeZone,
  timeZoneLabel,
  timeZoneOffsetLabel,
  viewerTimeZone,
} from '../timezone';

/**
 * Naming a zone, and nothing else. Nothing here schedules anything - the
 * backend resolves every slot against the organizer's own `TimeZoneInfo`, and
 * these helpers only label which of the two readings it already sent is being
 * shown.
 */
describe('timeZoneOffsetLabel', () => {
  it('reads the offset out of the platform tz database rather than assuming one', () => {
    // Deliberately two dates six months apart in a zone that observes DST: a
    // hardcoded offset is how a booking page ends up claiming GMT+1 all summer.
    const winter = timeZoneOffsetLabel('Europe/Skopje', new Date('2026-01-15T12:00:00Z'));
    const summer = timeZoneOffsetLabel('Europe/Skopje', new Date('2026-07-15T12:00:00Z'));

    expect(winter).toBe('GMT+1');
    expect(summer).toBe('GMT+2');
  });

  it('handles a zone with a half-hour offset', () => {
    expect(timeZoneOffsetLabel('Asia/Kolkata', new Date('2026-07-15T12:00:00Z'))).toBe('GMT+5:30');
  });

  it('returns null for an id the platform does not know, rather than throwing', () => {
    // A booking page whose organizer has a zone this browser has never heard of
    // must still render - the id itself is still shown.
    expect(timeZoneOffsetLabel('Not/AZone')).toBeNull();
  });
});

describe('timeZoneLabel', () => {
  it('keeps the IANA id and adds the offset, because the offset is the part people check', () => {
    expect(timeZoneLabel('Europe/Skopje', new Date('2026-07-15T12:00:00Z'))).toBe('Europe/Skopje (GMT+2)');
  });

  it('falls back to the bare id when no offset can be resolved', () => {
    expect(timeZoneLabel('Not/AZone')).toBe('Not/AZone');
  });
});

describe('isViewerTimeZone', () => {
  it('is true for the browser\'s own zone', () => {
    expect(isViewerTimeZone(viewerTimeZone())).toBe(true);
  });

  it('compares ids, not current offsets', () => {
    // Europe/London and Africa/Abidjan share an offset for part of the year and
    // are not the same zone. Comparing offsets would tell a Londoner in January
    // that 10:00 is "10:00 your time", which reads as a bug even when true.
    expect(isViewerTimeZone('Europe/London') && isViewerTimeZone('Africa/Abidjan')).toBe(false);
  });
});

describe('formatUtcOnViewerClock', () => {
  it('reads a real instant on the local clock', () => {
    const instant = '2026-08-20T07:00:00Z';

    // Derived from the same call the code makes rather than hardcoded: the
    // expected reading depends on the machine's zone and locale.
    expect(formatUtcOnViewerClock(instant)).toBe(
      new Date(instant).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
    );
  });
});
