import { useEffect } from 'react';

/**
 * What the browser tab says.
 *
 * Every route used to render the one `<title>` in `index.html` — "Booking
 * Session Tracker" — so a dozen open tabs were indistinguishable, browser
 * history was unsearchable, and a bookmark named itself after the product
 * rather than the page. That is a real cost on an app whose organizer screens
 * are all one click apart.
 *
 * **One hook and one suffix, called by the page itself**, rather than a
 * pathname→title table mounted once at the router. Two reasons, both practical:
 *
 *   - Several titles are only knowable from data the page fetched — the public
 *     booking page names the service and the organizer, and a route table
 *     cannot see either.
 *   - A table at the router level would fight the pages that do know better.
 *     Child effects run before parent effects in React, so the router's write
 *     would land *after* the page's and clobber it — a bug that only shows up
 *     on the routes that matter most.
 *
 * So the mechanism is this file: pages pass the leading segment, this composes
 * and applies it. Nothing else in the app assigns `document.title`.
 */
export const APP_NAME = 'BookingTracker';

/**
 * `"Working hours"` → `"Working hours · BookingTracker"`; nothing → the bare
 * product name, which is right for the landing page and for a screen still
 * loading the data its title depends on.
 */
export function pageTitle(segment?: string | null): string {
  const trimmed = segment?.trim();
  return trimmed ? `${trimmed} · ${APP_NAME}` : APP_NAME;
}

/**
 * Sets the tab title for as long as this component is mounted.
 *
 * Pass `null` while the data behind a title is still loading — the tab reads
 * `BookingTracker` until it is known, which is better than flashing a
 * placeholder or, worse, a stale name from the previous route.
 *
 * There is deliberately no cleanup that restores a previous title: the next
 * route sets its own on mount, and restoring on unmount would make the tab
 * flicker through the old title on every navigation.
 */
export function useDocumentTitle(segment?: string | null): void {
  useEffect(() => {
    document.title = pageTitle(segment);
  }, [segment]);
}
