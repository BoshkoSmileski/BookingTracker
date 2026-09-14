/**
 * The organizer app's information architecture, as data.
 *
 * Pure functions in `lib/` rather than markup inside the sidebar, so every
 * route's grouping and active state can be asserted directly, and so adding a
 * screen is one entry here rather than an edit in two components.
 *
 * The app has two genuinely different kinds of setting, and the previous
 * navigation could not say so: `Working hours`, `Blocked dates`, `Calendar` and
 * `Notifications` are **one per organizer** (one `WorkingSchedule`, one
 * `CalendarConnection`, one `NotificationSettings` row, one exception list),
 * even though their URLs sit under `/dashboard/{pageId}/settings/...`. Editing
 * any of them from a booking page changes every booking page. They are grouped
 * as **Workspace** here, separately from the booking page's own settings, so the
 * navigation states that instead of implying the opposite.
 *
 * The URLs themselves are unchanged: `CalendarIntegrationPage` passes its
 * `pageId` to the Google OAuth `connect` endpoint, which signs it into the OAuth
 * `state`, so a page id is a real part of that flow rather than an accident of
 * routing. Giving these four page-less routes is worth doing, but it is a
 * routing-and-backend change, not a navigation one. `workspaceSettingsPath`
 * below is the single place that decides which page id to borrow.
 */

export interface NavItem {
  /** Appended to `/dashboard/{pageId}`; empty string is the booking page's own dashboard. */
  segment: string;
  label: string;
}

/** First segments under `/dashboard` that name a screen, not a booking page id. */
const RESERVED_SEGMENTS = ['analytics', 'new'];

/** Settings that belong to **this booking page**, ordered by how often they are edited. */
export const PAGE_NAV: NavItem[] = [
  { segment: '', label: 'Sessions' },
  { segment: 'settings/details', label: 'Details' },
  { segment: 'settings/scheduling', label: 'Duration & buffers' },
  // Beside Duration rather than in Workspace: how a booking meets is set per
  // booking page (one page can be a Meet call while another is in person), so
  // grouping it with the organizer-wide settings would be the exact factual
  // error the Workspace split exists to correct.
  { segment: 'settings/meeting', label: 'Meeting' },
  { segment: 'settings/limits', label: 'Booking limits' },
  // Two neighbours on purpose: one holds what a visitor reads, the other what
  // they answer, and they are the two things an organizer edits together when
  // shaping the booking experience for this page.
  { segment: 'settings/instructions', label: 'Instructions' },
  { segment: 'settings/form', label: 'Booking form' },
];

/** Settings that are one-per-organizer and apply to every booking page. */
export const WORKSPACE_NAV: NavItem[] = [
  { segment: 'settings/hours', label: 'Working hours' },
  // Directly under Working hours, because that is what it departs from. One row
  // rather than the two ("Blocked dates" / "Date overrides") this replaced: they
  // are two implementations of one organizer question - what is different about
  // this date - and reading them apart made a single month impossible to see.
  // Workspace rather than page scope is a factual claim: both are one row per
  // organizer per date and change every booking page, like the weekly schedule.
  { segment: 'settings/date-exceptions', label: 'Date exceptions' },
  { segment: 'settings/calendar', label: 'Calendar' },
  { segment: 'settings/notifications', label: 'Notifications' },
];

const ALL_NAV = [...PAGE_NAV, ...WORKSPACE_NAV];

const NAV_LABELS = new Map(
  ALL_NAV.filter((item) => item.segment !== '').map((item) => [item.segment, item.label] as const),
);

const WORKSPACE_SEGMENTS = new Set(WORKSPACE_NAV.map((item) => item.segment));

/** The booking page id in the current URL, or null on an organizer-wide screen. */
export function bookingPageIdFromPath(pathname: string): string | null {
  const [dashboard, first] = segmentsOf(pathname);
  if (dashboard !== 'dashboard' || !first || RESERVED_SEGMENTS.includes(first)) return null;
  return first;
}

/**
 * The part of the path after the booking page id - `''` for the page's own
 * dashboard, `'settings/hours'` for a settings screen. Null off a booking page.
 */
export function pageSectionOf(pathname: string): string | null {
  if (bookingPageIdFromPath(pathname) === null) return null;
  const rest = segmentsOf(pathname).slice(2).join('/');
  // A session detail page still belongs to its page's session list.
  if (rest === '' || rest.startsWith('sessions')) return '';
  return NAV_LABELS.has(rest) ? rest : null;
}

/**
 * True when the current URL is one of the organizer-wide settings screens.
 *
 * The sidebar needs this to decide which group to mark, because such a screen
 * carries a booking page id in its URL while belonging to no booking page.
 */
export function isWorkspaceSettingsPath(pathname: string): boolean {
  const section = pageSectionOf(pathname);
  return section !== null && WORKSPACE_SEGMENTS.has(section);
}

/**
 * A link to an organizer-wide settings screen.
 *
 * These routes need *a* page id; which one is immaterial to what they show, so
 * the id already in scope is preferred (it keeps the URL stable while an
 * organizer moves between them) and the first owned page is the fallback. Null
 * when there is no page at all - a brand new organizer, whose only sensible
 * destination is creating one.
 */
export function workspaceSettingsPath(
  segment: string,
  currentPageId: string | null,
  firstPageId: string | null,
): string | null {
  const pageId = currentPageId ?? firstPageId;
  return pageId === null ? null : `/dashboard/${pageId}/${segment}`;
}

/** Human label for a screen, used as the document-level page name where a title is needed. */
export function navLabel(segment: string): string | undefined {
  return NAV_LABELS.get(segment);
}

function segmentsOf(pathname: string): string[] {
  return pathname.split('/').filter(Boolean);
}
