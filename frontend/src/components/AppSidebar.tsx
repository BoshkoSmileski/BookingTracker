import { Link, NavLink } from 'react-router-dom';
import {
  BarChart3, Bell, CalendarCheck2, CalendarRange, Clock, LayoutGrid, Plus,
} from 'lucide-react';
import { PAGE_NAV, WORKSPACE_NAV, workspaceSettingsPath } from '../lib/dashboardNav';
import { FOCUS_RING } from './ui';
import type { ComponentType, ReactNode } from 'react';
import type { BookingPageSummaryDto } from '../lib/types';

type Icon = ComponentType<{ className?: string }>;

/**
 * Every workspace row needs one: `ItemIcon` renders nothing without a match, and
 * an unmatched row's label then starts where every other row's icon does, which
 * reads as a broken indent rather than as a different kind of row. `Date
 * overrides` shipped that way until it was merged into `Date exceptions`.
 *
 * `CalendarRange` rather than `CalendarOff`, because the row now covers days
 * with different hours as well as days that are blocked.
 */
const WORKSPACE_ICONS: Record<string, Icon> = {
  'settings/hours': Clock,
  'settings/date-exceptions': CalendarRange,
  'settings/calendar': CalendarCheck2,
  'settings/notifications': Bell,
};

/**
 * The application's primary navigation.
 *
 * A left rail rather than a top bar because this app has two navigation levels
 * in play at once — the workspace, and whichever booking page is being worked
 * on. Expressed horizontally that was a nine-item strip that scrolled sideways
 * on a laptop; vertically it is one column with room to spare.
 *
 * Structure, top to bottom, in decreasing scope:
 *   Dashboard / Analytics   — the whole workspace
 *   Workspace               — the four one-per-organizer settings
 *   Booking pages           — the objects, with the open one expanded in place
 *   Account                 — who you are, and the way out
 *
 * `Workspace` is a genuine correction rather than a relabelling: those four
 * screens edit one row per organizer and affect every booking page, but their
 * URLs sit under a page. See lib/dashboardNav.ts.
 *
 * Sized as desktop navigation: 288px wide with 40px rows, up from a 240px rail
 * with 30px rows. The rail is the one piece of chrome present on every screen,
 * so it sets the perceived scale of the whole application — undersize it and
 * everything else reads as a compact utility panel regardless of its own sizing.
 */
export function AppSidebar({
  pages,
  activePageId,
  contextPageId,
  organizerName,
  organizerEmail,
  onNavigate,
  onSignOut,
}: {
  pages: BookingPageSummaryDto[] | null;
  /**
   * The booking page actually being worked on — highlighted and expanded. Null
   * on Dashboard, Analytics, and on the organizer-wide settings, because those
   * are not about any one page even when one is named in the URL.
   */
  activePageId: string | null;
  /** The page id to borrow when building organizer-wide settings links. */
  contextPageId: string | null;
  organizerName?: string;
  organizerEmail?: string;
  /** Called on any navigation, so the mobile drawer can close itself. */
  onNavigate?: () => void;
  onSignOut: () => void;
}) {
  const firstPageId = pages?.[0]?.id ?? null;
  const pagesLoading = pages === null;

  return (
    <div className="flex h-full flex-col bg-shell-50">
      <div className="flex h-16 shrink-0 items-center border-b border-shell-200 px-5">
        <Link
          to="/dashboard"
          onClick={onNavigate}
          className={`flex items-center gap-2.5 rounded-lg py-1 text-[15px] font-semibold tracking-tight text-gray-900 ${FOCUS_RING}`}
        >
          {/* A small solid mark rather than a logotype alone: it gives the rail
              a fixed anchor point at the top and carries the accent into the
              one place it is always visible. */}
          <span
            aria-hidden="true"
            className="flex h-7 w-7 items-center justify-center rounded-[7px] bg-accent-600 text-[13px] font-bold text-white"
          >
            B
          </span>
          BookingTracker
        </Link>
      </div>

      <nav aria-label="Main" className="min-h-0 flex-1 overflow-y-auto px-3 py-4">
        <ul className="space-y-1">
          <li>
            <NavItemLink to="/dashboard" icon={LayoutGrid} end onNavigate={onNavigate}>
              Dashboard
            </NavItemLink>
          </li>
          <li>
            <NavItemLink to="/dashboard/analytics" icon={BarChart3} onNavigate={onNavigate}>
              Analytics
            </NavItemLink>
          </li>
        </ul>

        <GroupLabel>Workspace</GroupLabel>
        <ul className="space-y-1">
          {WORKSPACE_NAV.map((item) => {
            const to = workspaceSettingsPath(item.segment, contextPageId, firstPageId);
            // These routes need a page id to build, and there are two different
            // reasons one might be missing which must not look alike: the list
            // is still loading (momentary — greying the group makes the whole
            // section read as switched off), or the organizer genuinely has no
            // booking page yet (real, and worth saying why).
            if (to === null) {
              return (
                <li key={item.segment}>
                  <span
                    aria-disabled="true"
                    title={pagesLoading ? undefined : 'Create a booking page first.'}
                    className={`flex cursor-default items-center gap-3 rounded-lg px-3 py-2 text-sm ${
                      pagesLoading ? 'text-shell-700' : 'text-shell-600/60'
                    }`}
                  >
                    <ItemIcon icon={WORKSPACE_ICONS[item.segment]} />
                    {item.label}
                  </span>
                </li>
              );
            }
            return (
              <li key={item.segment}>
                <NavItemLink to={to} icon={WORKSPACE_ICONS[item.segment]} onNavigate={onNavigate}>
                  {item.label}
                </NavItemLink>
              </li>
            );
          })}
        </ul>

        <GroupLabel
          action={
            <Link
              to="/dashboard/new"
              onClick={onNavigate}
              aria-label="New booking page"
              title="New booking page"
              className={`rounded-md p-1 text-shell-600 transition-colors hover:bg-shell-200 hover:text-gray-900 ${FOCUS_RING}`}
            >
              <Plus className="h-4 w-4" aria-hidden="true" />
            </Link>
          }
        >
          Booking pages
        </GroupLabel>

        {pagesLoading && (
          <div className="space-y-2 px-3 py-2" role="status" aria-label="Loading booking pages">
            {['70%', '55%'].map((w) => (
              <div key={w} className="h-4 animate-pulse rounded bg-shell-200" style={{ width: w }} />
            ))}
          </div>
        )}

        {pages?.length === 0 && (
          <p className="px-3 py-2 text-[13px] text-shell-600">No booking pages yet.</p>
        )}

        <ul className="space-y-1">
          {pages?.map((page) => {
            const isOpen = page.id === activePageId;
            return (
              <li key={page.id}>
                {/* `end` so the row is not marked merely because the URL starts
                    with this page's id — an organizer-wide settings screen does,
                    and is not about this page. `forceActive` re-marks it for the
                    page's own settings, which is the parent relationship. */}
                <NavItemLink to={`/dashboard/${page.id}`} end onNavigate={onNavigate} forceActive={isOpen}>
                  {/* A dot where the other rows have their icon, so every label
                      in the rail starts on the same vertical line. Without it a
                      booking page title began 30px left of "Working hours"
                      directly above it, which read as a broken indent rather
                      than as a different kind of row. It doubles as the page's
                      live/disabled state, which is why this is a dot and not a
                      borrowed glyph: a second icon would say "another feature",
                      where these rows are objects. */}
                  <span
                    aria-hidden="true"
                    className={`ml-[5px] mr-[5px] h-2 w-2 shrink-0 rounded-full ${
                      page.isActive ? 'bg-accent-500' : 'bg-shell-300'
                    }`}
                  />
                  <span className="truncate" title={page.title}>{page.title}</span>
                  {!page.isActive && (
                    <span className="ml-auto shrink-0 text-[12px] font-normal text-shell-600">off</span>
                  )}
                </NavItemLink>

                {/* Only the page being worked on expands. Showing every page's
                    settings at once would put five times the pages' worth of
                    links in the rail, and nothing else on screen is about the
                    other pages. */}
                {isOpen && (
                  <ul className="mt-1 ml-4 space-y-0.5 border-l border-shell-200 pl-3">
                    {PAGE_NAV.map((item) => (
                      <li key={item.segment || 'sessions'}>
                        <NavItemLink
                          to={item.segment === '' ? `/dashboard/${page.id}` : `/dashboard/${page.id}/${item.segment}`}
                          end={item.segment === ''}
                          onNavigate={onNavigate}
                          nested
                        >
                          {item.label}
                        </NavItemLink>
                      </li>
                    ))}
                  </ul>
                )}
              </li>
            );
          })}
        </ul>
      </nav>

      <div className="shrink-0 border-t border-shell-200 p-3">
        <div className="flex items-center gap-3 px-1 pb-2">
          {organizerName && (
            <>
              <span
                aria-hidden="true"
                className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-shell-200 text-[13px] font-semibold text-shell-700"
              >
                {organizerName.charAt(0).toUpperCase()}
              </span>
              <span className="min-w-0">
                <span className="block truncate text-sm font-medium text-gray-900" title={organizerName}>
                  {organizerName}
                </span>
                {organizerEmail && (
                  <span className="block truncate text-[12px] text-shell-600" title={organizerEmail}>
                    {organizerEmail}
                  </span>
                )}
              </span>
            </>
          )}
        </div>
        <button
          type="button"
          onClick={onSignOut}
          className={`w-full rounded-lg px-3 py-2 text-left text-sm text-shell-700 transition-colors hover:bg-shell-200 hover:text-gray-900 ${FOCUS_RING}`}
        >
          Sign out
        </button>
      </div>
    </div>
  );
}

/**
 * One navigable row.
 *
 * `NavLink` supplies the active state from the router rather than from a
 * hand-rolled pathname comparison, so it cannot drift from what is rendered.
 * `forceActive` is the one exception: a booking page's own row stays marked
 * while one of its settings screens is open, a parent relationship `NavLink`
 * has no way to express for sibling paths.
 *
 * Active state is an accent tint plus an accent left marker, not a grey fill.
 * A grey fill is the same colour family as the rail itself, so at a glance the
 * active row reads as slightly-different-background rather than as *the one you
 * are on*; the marker survives that glance.
 */
function NavItemLink({
  to,
  icon,
  end,
  children,
  onNavigate,
  forceActive,
  nested,
}: {
  to: string;
  icon?: Icon;
  end?: boolean;
  children: ReactNode;
  onNavigate?: () => void;
  forceActive?: boolean;
  nested?: boolean;
}) {
  return (
    <NavLink
      to={to}
      end={end}
      onClick={onNavigate}
      className={({ isActive }) => {
        const active = isActive || forceActive;
        // The left marker is suppressed on nested rows: they already sit inside
        // a guide line, and a second bar a few pixels from it reads as a glitch
        // rather than as emphasis. The tint and weight carry the state there.
        const marker = nested
          ? ''
          : 'before:absolute before:left-0 before:top-1/2 before:h-5 before:w-[3px] before:-translate-y-1/2 before:rounded-r-full before:bg-accent-600';
        return `relative flex items-center gap-3 rounded-lg py-2 text-sm transition-colors ${FOCUS_RING} px-3 ${
          active ? `bg-accent-50 font-medium text-accent-800 ${marker}` : 'text-shell-700 hover:bg-shell-100 hover:text-gray-900'
        }`;
      }}
    >
      {icon && <ItemIcon icon={icon} />}
      {children}
    </NavLink>
  );
}

/**
 * Decorative throughout: every row that renders one also renders its own text
 * label beside it, which is the row's accessible name.
 *
 * `aria-hidden` is written out here for consistency with the ~60 other icon
 * sites and with `DashboardSummary`/`HomePage`, the two indirect renderers that
 * already had it - not because it changes anything. lucide-react applies
 * `aria-hidden="true"` itself to any icon carrying no `aria-*`, `role` or
 * `title` prop, so these were never exposed. Being explicit only means a reader
 * can tell a decorative icon from one deliberately left announceable.
 */
function ItemIcon({ icon: IconComponent }: { icon?: Icon }) {
  if (!IconComponent) return null;
  return <IconComponent className="h-[18px] w-[18px] shrink-0" aria-hidden="true" />;
}

function GroupLabel({ children, action }: { children: string; action?: ReactNode }) {
  return (
    <div className="mt-7 mb-2 flex items-center justify-between gap-2 px-3">
      <span className="text-[11px] font-semibold uppercase tracking-[0.08em] text-shell-600">{children}</span>
      {action}
    </div>
  );
}
