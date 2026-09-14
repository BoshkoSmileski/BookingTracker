import { useEffect, useRef, useState } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import { Menu, X } from 'lucide-react';
import { useAuth } from '../contexts/AuthContext';
import { api } from '../lib/api';
import { bookingPageIdFromPath, isWorkspaceSettingsPath } from '../lib/dashboardNav';
import { AppSidebar } from './AppSidebar';
import { FOCUS_RING, MAIN_CONTENT_ID, SkipLink } from './ui';
import type { BookingPageSummaryDto } from '../lib/types';

/**
 * What the layout hands down to its child routes.
 *
 * `pages` is `null` while the one per-session fetch is in flight and `[]` if it
 * failed - the same tri-state the rail reads, so a child can tell "not known
 * yet" from "this organizer has none" instead of guessing from a length.
 */
export interface DashboardOutletContext {
  pages: BookingPageSummaryDto[] | null;
}

/**
 * The organizer application shell: a permanent left rail beside a scrolling
 * content pane.
 *
 * Mounted as a pathless layout route in App.tsx, which is what lets it survive
 * navigation between screens - so the booking page list, which the rail lists
 * and the content does not, is fetched once per session rather than once per
 * screen - and what lets ProtectedRoute be declared once instead of per route.
 *
 * The rail is fixed and only the content pane scrolls (`h-screen` +
 * `overflow-y-auto`), which is the behaviour that distinguishes an application
 * window from a long web page: navigation does not slide away when you read.
 * Below `lg` there is not room for a permanent rail, so it becomes a drawer over
 * the content, opened from a compact bar that exists only at that size.
 */
export function DashboardLayout() {
  const { organizer, callProtected, logout } = useAuth();
  const { pathname } = useLocation();

  const pageId = bookingPageIdFromPath(pathname);
  const [pages, setPages] = useState<BookingPageSummaryDto[] | null>(null);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const closeButtonRef = useRef<HTMLButtonElement>(null);
  // Which page id we have already asked the API about, so an id that genuinely
  // is not in the list (deleted, or another organizer's) is fetched once rather
  // than on every render.
  const requestedRef = useRef<string | null>(null);

  useEffect(() => {
    const key = pageId ?? '';
    const isKnown = pages?.some((p) => p.id === pageId) ?? false;
    if (pages !== null && (pageId === null || isKnown)) return;
    if (requestedRef.current === key) return;

    requestedRef.current = key;
    callProtected((token) => api.organizer.getMyBookingPages(token))
      // Navigation itself never depends on this - only the rail's list of page
      // names does - so a failure degrades rather than being surfaced.
      .then(setPages)
      .catch(() => setPages([]));
  }, [pageId, pages, callProtected]);

  // Escape is the expected way out of an overlay, and without it a keyboard user
  // has to tab through the whole rail to reach the close button.
  useEffect(() => {
    if (!drawerOpen) return;
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setDrawerOpen(false);
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [drawerOpen]);

  // Opening the drawer moves focus into it, so the next Tab continues inside the
  // overlay rather than behind it.
  useEffect(() => {
    if (drawerOpen) closeButtonRef.current?.focus();
  }, [drawerOpen]);

  // Working hours, blocked dates, calendar and notifications carry a page id in
  // their URL but belong to the organizer, not to that page - so the page is the
  // link-building context without being the page you are working on. Marking it
  // active there would claim you were editing that one page's settings.
  const onWorkspaceSettings = isWorkspaceSettingsPath(pathname);

  const sidebar = (
    <AppSidebar
      pages={pages}
      activePageId={onWorkspaceSettings ? null : pageId}
      contextPageId={pageId}
      organizerName={organizer?.name}
      organizerEmail={organizer?.email}
      onNavigate={() => setDrawerOpen(false)}
      onSignOut={() => void logout()}
    />
  );

  return (
    <div className="flex h-screen overflow-hidden bg-white">
      {/* First in the DOM, so it is the very first thing Tab reaches. The rail
          below it is fourteen links deep. */}
      <SkipLink />
      {/* Permanent rail. `hidden lg:flex` rather than a CSS transform, so on a
          small screen it is genuinely absent from the tab order, not just off-screen. */}
      <aside className="hidden w-72 shrink-0 border-r border-shell-200 lg:flex lg:flex-col">
        {sidebar}
      </aside>

      {drawerOpen && (
        <div className="fixed inset-0 z-40 lg:hidden">
          {/* The scrim is a pointer convenience, not a control: it is hidden
              from assistive tech so it does not duplicate the close button's
              name, and keyboard users have Escape and that button instead. */}
          <div
            aria-hidden="true"
            onClick={() => setDrawerOpen(false)}
            className="absolute inset-0 bg-gray-900/20"
          />
          <div className="absolute inset-y-0 left-0 flex w-72 flex-col border-r border-shell-200 shadow-xl">
            <button
              ref={closeButtonRef}
              type="button"
              onClick={() => setDrawerOpen(false)}
              className={`absolute right-3 top-4 z-10 rounded-md p-1.5 text-shell-600 hover:bg-shell-200 hover:text-gray-900 ${FOCUS_RING}`}
            >
              <X className="h-5 w-5" aria-hidden="true" />
              <span className="sr-only">Close navigation</span>
            </button>
            {sidebar}
          </div>
        </div>
      )}

      <div className="flex min-w-0 flex-1 flex-col">
        <div className="flex h-16 shrink-0 items-center gap-3 border-b border-shell-200 bg-shell-50 px-4 lg:hidden">
          <button
            type="button"
            onClick={() => setDrawerOpen(true)}
            aria-expanded={drawerOpen}
            className={`rounded-lg p-2 text-shell-700 hover:bg-shell-200 hover:text-gray-900 ${FOCUS_RING}`}
          >
            <Menu className="h-5 w-5" aria-hidden="true" />
            <span className="sr-only">Open navigation</span>
          </button>
          <span className="text-[15px] font-semibold tracking-tight text-gray-900">BookingTracker</span>
        </div>

        {/*
          The one scroll container. Every page renders content only — no
          background, no container width, no navigation of its own.

          1440px rather than the 1024px this was: on a 24" display a 1024px
          column left roughly a third of the window permanently blank, and the
          list and analytics screens have genuine content to put there. Editing
          screens opt into a narrower, centred measure via FORM_COLUMN instead of
          inheriting a width that suits a table.
        */}
        {/* `tabIndex={-1}` so the skip link actually moves focus here rather
            than only scrolling; `focus:outline-none` because the destination of
            a skip is not itself a control and should not draw a ring. */}
        <main
          id={MAIN_CONTENT_ID}
          tabIndex={-1}
          className="min-h-0 flex-1 overflow-y-auto focus:outline-none"
        >
          <div className="mx-auto w-full max-w-[1440px] px-6 py-8 lg:px-10 lg:py-10">
            {/*
              The booking page list the rail already fetched, offered to the
              screens below rather than re-requested by each of them. React
              Router's own idiom for exactly this - see `useDashboardPages`.
            */}
            <Outlet context={{ pages } satisfies DashboardOutletContext} />
          </div>
        </main>
      </div>
    </div>
  );
}
