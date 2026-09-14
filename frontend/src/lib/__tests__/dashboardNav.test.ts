import { describe, expect, it } from 'vitest';
import {
  PAGE_NAV,
  WORKSPACE_NAV,
  bookingPageIdFromPath,
  isWorkspaceSettingsPath,
  navLabel,
  pageSectionOf,
  workspaceSettingsPath,
} from '../dashboardNav';

const PAGE = 'a1b2c3';

describe('bookingPageIdFromPath', () => {
  it('reads the id off a page-scoped route', () => {
    expect(bookingPageIdFromPath(`/dashboard/${PAGE}`)).toBe(PAGE);
    expect(bookingPageIdFromPath(`/dashboard/${PAGE}/settings/hours`)).toBe(PAGE);
    expect(bookingPageIdFromPath(`/dashboard/${PAGE}/sessions/s1`)).toBe(PAGE);
  });

  it('does not mistake the organizer-wide screens for a booking page id', () => {
    expect(bookingPageIdFromPath('/dashboard')).toBeNull();
    expect(bookingPageIdFromPath('/dashboard/analytics')).toBeNull();
    expect(bookingPageIdFromPath('/dashboard/new')).toBeNull();
  });
});

describe('pageSectionOf', () => {
  it('names the open section', () => {
    expect(pageSectionOf(`/dashboard/${PAGE}/settings/hours`)).toBe('settings/hours');
    expect(pageSectionOf(`/dashboard/${PAGE}/settings/limits`)).toBe('settings/limits');
  });

  it('treats the page dashboard and a session inside it as the same Sessions section', () => {
    expect(pageSectionOf(`/dashboard/${PAGE}`)).toBe('');
    expect(pageSectionOf(`/dashboard/${PAGE}/sessions/s1`)).toBe('');
  });

  it('is null off a booking page, and for a settings path that is not a section', () => {
    expect(pageSectionOf('/dashboard/analytics')).toBeNull();
    expect(pageSectionOf(`/dashboard/${PAGE}/settings/unknown`)).toBeNull();
  });
});

describe('the two groups are genuinely different kinds of setting', () => {
  it('keeps one-per-organizer settings out of the booking page group', () => {
    // Working hours, blocked dates, calendar and notifications each edit a single
    // row per organizer. Listing them beside a page's own settings is what the
    // old tab strip had to caption its way around.
    const pageSegments = PAGE_NAV.map((i) => i.segment);
    for (const item of WORKSPACE_NAV) {
      expect(pageSegments).not.toContain(item.segment);
      expect(isWorkspaceSettingsPath(`/dashboard/${PAGE}/${item.segment}`)).toBe(true);
    }
  });

  it('does not treat a booking page section as organizer-wide', () => {
    expect(isWorkspaceSettingsPath(`/dashboard/${PAGE}`)).toBe(false);
    expect(isWorkspaceSettingsPath(`/dashboard/${PAGE}/settings/limits`)).toBe(false);
    expect(isWorkspaceSettingsPath('/dashboard/analytics')).toBe(false);
  });
});

describe('workspaceSettingsPath', () => {
  it('prefers the page already in scope, so the URL stays put while moving between them', () => {
    expect(workspaceSettingsPath('settings/hours', PAGE, 'other'))
      .toBe(`/dashboard/${PAGE}/settings/hours`);
  });

  it('borrows the first owned page when no page is in scope', () => {
    expect(workspaceSettingsPath('settings/calendar', null, 'first'))
      .toBe('/dashboard/first/settings/calendar');
  });

  it('has nowhere to point when the organizer owns no pages at all', () => {
    // These routes need a page id; a brand new organizer has none, and the only
    // sensible destination is creating one.
    expect(workspaceSettingsPath('settings/hours', null, null)).toBeNull();
  });
});

describe('navLabel', () => {
  it('resolves a label for every navigable segment', () => {
    for (const item of [...PAGE_NAV, ...WORKSPACE_NAV]) {
      if (item.segment === '') continue;
      expect(navLabel(item.segment)).toBe(item.label);
    }
  });

  it('is undefined for a segment that is not navigation', () => {
    expect(navLabel('settings/nope')).toBeUndefined();
  });
});
