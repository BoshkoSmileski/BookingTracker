import { describe, expect, it } from 'vitest';
import {
  MEETING_PROVIDER_LABELS,
  MEETING_PROVIDER_OPTIONS,
  hasMeeting,
  meetingProviderLabel,
} from '../meeting';

describe('meetingProviderLabel', () => {
  it('names Google Meet the way the backend emails and the ICS LABEL do', () => {
    // The label crosses into the email subject line and the ICS CONFERENCE
    // LABEL parameter, so a rename here is a rename in three systems.
    expect(meetingProviderLabel('GoogleMeet')).toBe('Google Meet');
  });

  it('returns null for an in-person booking, so callers render nothing rather than the word "None"', () => {
    expect(meetingProviderLabel('None')).toBeNull();
    expect(meetingProviderLabel(null)).toBeNull();
    expect(meetingProviderLabel(undefined)).toBeNull();
  });
});

describe('hasMeeting', () => {
  it('is true only when both a provider and a URL are present', () => {
    expect(hasMeeting({ meetingProvider: 'GoogleMeet', meetingUrl: 'https://meet.google.com/abc' })).toBe(true);
  });

  it('is false when the provider asked for a meeting but no link was ever created', () => {
    // The real degraded state: the page is set to Google Meet, but the
    // organizer had no connected calendar (or Google was down) when the
    // booking was made. There is nothing to link to.
    expect(hasMeeting({ meetingProvider: 'GoogleMeet', meetingUrl: null })).toBe(false);
  });

  it('is false for an in-person booking', () => {
    expect(hasMeeting({ meetingProvider: null, meetingUrl: null })).toBe(false);
    expect(hasMeeting({ meetingProvider: 'None', meetingUrl: null })).toBe(false);
  });

  it('is false for a URL with no provider, so a half-written record never renders a nameless button', () => {
    expect(hasMeeting({ meetingProvider: null, meetingUrl: 'https://meet.google.com/abc' })).toBe(false);
  });
});

describe('MEETING_PROVIDER_OPTIONS', () => {
  it('offers in person first, matching the default a new booking page has', () => {
    expect(MEETING_PROVIDER_OPTIONS.map((o) => o.value)).toEqual(['None', 'GoogleMeet']);
  });

  it('labels every option from the shared map rather than repeating the strings', () => {
    for (const option of MEETING_PROVIDER_OPTIONS) {
      expect(option.label).toBe(MEETING_PROVIDER_LABELS[option.value]);
    }
  });
});
