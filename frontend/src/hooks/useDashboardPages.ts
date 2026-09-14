import { useOutletContext } from 'react-router-dom';
import type { DashboardOutletContext } from '../components/DashboardLayout';

/**
 * The organizer's booking pages, as already fetched once by `DashboardLayout`.
 *
 * The layout stays mounted across navigations, so this costs nothing: a screen
 * that needs a page's slug or title gets it without a second request. Returns
 * `null` while the fetch is in flight, if it failed, **and** when the component
 * is rendered outside the layout (a test mounting a page on its own) - all
 * three mean "not known", and every caller must render without it rather than
 * block on it.
 */
export function useDashboardPages(): DashboardOutletContext['pages'] {
  // `useOutletContext` returns null outside an Outlet rather than throwing, so
  // this stays safe for a page mounted directly.
  const context = useOutletContext<DashboardOutletContext | null>();
  return context?.pages ?? null;
}
