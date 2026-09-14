import { describe, expect, it } from 'vitest';
import {
  DEFAULT_SESSION_STATUS,
  SESSION_TABS,
  parseSessionStatus,
  sessionDetailPath,
  sessionListPath,
  sessionStatusSearch,
} from '../sessionFilter';

describe('parseSessionStatus', () => {
  it.each(SESSION_TABS)('round-trips %s', (status) => {
    expect(parseSessionStatus(status)).toBe(status);
  });

  it('falls back to the default rather than showing an empty list', () => {
    expect(parseSessionStatus(null)).toBe(DEFAULT_SESSION_STATUS);
    expect(parseSessionStatus(undefined)).toBe(DEFAULT_SESSION_STATUS);
    expect(parseSessionStatus('')).toBe(DEFAULT_SESSION_STATUS);
    expect(parseSessionStatus('Submitte')).toBe(DEFAULT_SESSION_STATUS);
    expect(parseSessionStatus('submitted')).toBe(DEFAULT_SESSION_STATUS);
  });
});

describe('sessionStatusSearch', () => {
  it('leaves the default out of the URL, so the common case stays clean', () => {
    expect(sessionStatusSearch(DEFAULT_SESSION_STATUS)).toBe('');
  });

  it('emits a param for anything else, and parses back to the same value', () => {
    const search = sessionStatusSearch('Cancelled');
    expect(search).toBe('?status=Cancelled');
    expect(parseSessionStatus(new URLSearchParams(search).get('status'))).toBe('Cancelled');
  });
});

describe('paths', () => {
  it('builds a list path that carries the filter', () => {
    expect(sessionListPath('page-1', 'Submitted')).toBe('/dashboard/page-1?status=Submitted');
    expect(sessionListPath('page-1', DEFAULT_SESSION_STATUS)).toBe('/dashboard/page-1');
  });

  it('carries the filter into the session, so Back can return to the same list', () => {
    // REGRESSION: opening a session from the Submitted tab and coming back used
    // to land on Active, because the filter only ever lived in component state.
    expect(sessionDetailPath('page-1', 'session-9', 'Submitted'))
      .toBe('/dashboard/page-1/sessions/session-9?status=Submitted');
  });

  it('round-trips a detail path back to the list it came from', () => {
    const detail = sessionDetailPath('page-1', 'session-9', 'Abandoned');
    const status = parseSessionStatus(new URLSearchParams(detail.split('?')[1]).get('status'));
    expect(sessionListPath('page-1', status)).toBe('/dashboard/page-1?status=Abandoned');
  });
});
