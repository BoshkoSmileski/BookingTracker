import type { BookingSessionStatus } from './types';

/**
 * The session list's filter, expressed as a URL search param rather than
 * component state.
 *
 * The filter used to live in `useState` on OrganizerDashboardPage, which meant
 * opening a session and coming back always landed on "Active" - the component
 * remounted and re-took its default. Putting it in the URL makes the filter part
 * of the navigation history, so the app's own Back link and the browser's Back
 * button return to the same list, and a filtered list can be linked or reloaded.
 *
 * Both the list and the session detail page read the param through here so they
 * can never disagree about the spelling of a status or about which one is the
 * default.
 */
export const SESSION_STATUS_PARAM = 'status';

export const SESSION_TABS: BookingSessionStatus[] = ['Active', 'Submitted', 'Cancelled', 'Abandoned'];

export const DEFAULT_SESSION_STATUS: BookingSessionStatus = 'Active';

/** Anything unrecognised (or absent) falls back to the default rather than showing an empty list. */
export function parseSessionStatus(value: string | null | undefined): BookingSessionStatus {
  return SESSION_TABS.find((tab) => tab === value) ?? DEFAULT_SESSION_STATUS;
}

/**
 * The default is left out of the URL so the common case stays `/dashboard/{id}`.
 * `parseSessionStatus` reads a missing param as the default, so the two round-trip.
 */
export function sessionStatusSearch(status: BookingSessionStatus): string {
  return status === DEFAULT_SESSION_STATUS ? '' : `?${SESSION_STATUS_PARAM}=${status}`;
}

export function sessionListPath(pageId: string, status: BookingSessionStatus): string {
  return `/dashboard/${pageId}${sessionStatusSearch(status)}`;
}

/**
 * The filter travels with the link into the session, so the detail page can send
 * the organizer back to the list they actually came from - including after a
 * reload, or when the link is shared, which `location.state` would not survive.
 */
export function sessionDetailPath(pageId: string, sessionId: string, status: BookingSessionStatus): string {
  return `/dashboard/${pageId}/sessions/${sessionId}${sessionStatusSearch(status)}`;
}
