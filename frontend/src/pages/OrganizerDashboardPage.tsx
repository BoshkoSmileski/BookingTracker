import { useCallback, useEffect, useState } from 'react';
import { Link, useLocation, useParams, useSearchParams } from 'react-router-dom';
import { ChevronRight, Inbox } from 'lucide-react';
import { DashboardSummary } from '../components/DashboardSummary';
import { NewBookingPagePanel } from '../components/NewBookingPagePanel';
import { PublicLinkBar } from '../components/PublicLinkBar';
import { StatusBadge } from '../components/StatusBadge';
import { CARD, EmptyState, FOCUS_RING, LoadError, PageHeader, SkeletonLines } from '../components/ui';
import { useAuth } from '../contexts/AuthContext';
import { useDashboardPages } from '../hooks/useDashboardPages';
import { useOrganizerTimeZone } from '../hooks/useOrganizerTimeZone';
import { api, errorMessage } from '../lib/api';
import { formatCalendarDateTime, organizerDateTime } from '../lib/calendarDates';
import { formatRelative } from '../lib/dates';
import {
  SESSION_STATUS_PARAM,
  SESSION_TABS,
  parseSessionStatus,
  sessionDetailPath,
} from '../lib/sessionFilter';
import { createDashboardConnection } from '../lib/signalr';
import type { HubConnection } from '@microsoft/signalr';
import type { BookingSessionDto, BookingSessionStatus } from '../lib/types';
import { useDocumentTitle } from '../lib/pageTitle';

/**
 * Is this booking still ahead, on the ORGANIZER's clock?
 *
 * `selectedDate`/`selectedTime` are organizer wall-clock columns, so this used
 * to be wrong twice over: `new Date("2026-08-14T09:00:00")` parses them in the
 * *browser's* zone, and it was compared against the browser's now. An organizer
 * working from a different zone than their configured one saw bookings labelled
 * Completed that had not happened yet, and Upcoming ones that had.
 *
 * Comparing the two wall-clock strings directly is what avoids reconstructing
 * an instant at all - both sides are now `yyyy-MM-dd` + `HH:mm:ss` on the same
 * clock, and lexical order is chronological order for those formats.
 */
function isUpcoming(session: BookingSessionDto, timeZoneId: string | null): boolean {
  if (!session.selectedDate || !session.selectedTime) return false;
  const now = organizerDateTime(timeZoneId);
  return `${session.selectedDate}T${session.selectedTime}` > `${now.date}T${now.time}`;
}

export function OrganizerDashboardPage() {
  useDocumentTitle('Sessions');
  const { pageId = '' } = useParams<{ pageId: string }>();
  const { callProtected, getAccessToken } = useAuth();
  // Falls back to the browser's clock while loading, exactly as before.
  const organizerTimeZone = useOrganizerTimeZone();
  // From the list the shell already holds - no extra request, and null until it
  // is known, in which case the link strip simply does not render.
  const slug = useDashboardPages()?.find((p) => p.id === pageId)?.slug ?? null;
  const [searchParams, setSearchParams] = useSearchParams();
  // Set by the create form, and only by it - so the "what next" panel below
  // belongs to the page that was just made and never to one revisited later.
  const createdSlug = (useLocation().state as { createdSlug?: string } | null)?.createdSlug;
  const [sessions, setSessions] = useState<BookingSessionDto[]>([]);
  const [loading, setLoading] = useState(true);
  /** Whether this tab has ever been read. An empty tab is a normal outcome. */
  const [loaded, setLoaded] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);

  // The filter lives in the URL, not in state: opening a session and coming
  // back used to remount this component and silently reset the tab to Active.
  const status = parseSessionStatus(searchParams.get(SESSION_STATUS_PARAM));

  const selectStatus = (next: BookingSessionStatus) => {
    const params = new URLSearchParams(searchParams);
    if (next === parseSessionStatus(null)) params.delete(SESSION_STATUS_PARAM);
    else params.set(SESSION_STATUS_PARAM, next);
    // `replace` so switching tabs does not stack history entries - the browser's
    // Back button then leaves this screen, exactly like the in-app crumb does,
    // instead of walking backwards through every tab that was tried.
    setSearchParams(params, { replace: true });
  };

  /**
   * The one screen where a swallowed failure was actively misleading: with no
   * `catch`, a failed load left the skeleton up for ever, and had it resolved
   * empty it would have read "No submitted sessions" - a claim about this
   * organizer's bookings rather than about a request that never came back.
   *
   * Also driven by SignalR below, where the list is already on screen, so the
   * error and the data have to be able to coexist.
   */
  const refresh = useCallback(async () => {
    try {
      const result = await callProtected((token) => api.organizer.getSessions(token, pageId, status));
      setSessions(result);
      setLoaded(true);
      setLoadError(null);
    } catch (e) {
      setLoadError(errorMessage(e, 'Could not load these sessions.'));
    } finally {
      setLoading(false);
    }
  }, [callProtected, pageId, status]);

  useEffect(() => {
    setLoading(true);
    // Switching tabs is a fresh question, so a previous tab's failure must not
    // be left on screen describing this one.
    setLoaded(false);
    setLoadError(null);
    void refresh();
  }, [refresh]);

  // Live updates: any event on any session belonging to this page triggers a
  // refetch of the current tab, so a visitor typing shows up on the dashboard
  // without the organizer ever reloading the page.
  useEffect(() => {
    let connection: HubConnection | undefined = createDashboardConnection(getAccessToken);
    connection.on('SessionUpdated', () => void refresh());
    connection.on('SessionEventAdded', () => void refresh());

    connection
      .start()
      .then(() => connection?.invoke('JoinBookingPageGroup', pageId))
      .catch(() => {
        // Live updates are a nice-to-have; the dashboard still works from the
        // initial fetch (and a manual refresh) if the hub is unreachable or
        // rejects the join (e.g. not signed in, or not this page's owner).
      });

    return () => {
      void connection?.stop();
      connection = undefined;
    };
  }, [pageId, refresh, getAccessToken]);

  return (
    <>
      <PageHeader title="Sessions" description="Every visitor who has opened this booking page, live." />

      {createdSlug && <NewBookingPagePanel pageId={pageId} slug={createdSlug} />}

      {/* Not both: the just-created panel states the same URL louder and with
          more context, so the quiet strip would be a second copy of it. */}
      {!createdSlug && slug && <PublicLinkBar slug={slug} />}

      <DashboardSummary pageId={pageId} />

      {/*
        An underlined filter bar rather than filled pills: these switch a view
        rather than commit an action, and a solid pill reads as the primary
        button on the screen when it is not.

        A toggle-button group, not `role="tablist"`. It was marked up as tabs,
        which promises a screen-reader user two things this is not: a tabpanel
        the tab controls, and arrow-key movement between tabs. Neither existed,
        so the markup described a widget that was not there. `aria-pressed` says
        exactly what these are - four filters, one of them on.

        The selected tab used to carry a count badge, which has been removed
        rather than extended to the others: the count was only ever shown for the
        tab you were already on, and the summary band directly above already
        states Confirmed / Filling in now / Abandoned at four times the size.
        Two copies of the same number, one of them incomplete.
      */}
      <div
        className="mb-5 flex flex-wrap items-center gap-7 border-b border-gray-200"
        role="group"
        aria-label="Filter sessions by status"
      >
        {SESSION_TABS.map((tab) => (
          <button
            key={tab}
            type="button"
            aria-pressed={status === tab}
            onClick={() => selectStatus(tab)}
            className={`-mb-px border-b-2 px-0.5 pb-3 text-[15px] transition-colors ${FOCUS_RING} ${
              status === tab
                ? 'border-accent-600 font-semibold text-accent-800'
                : 'border-transparent text-gray-500 hover:border-gray-300 hover:text-gray-900'
            }`}
          >
            {tab}
          </button>
        ))}
      </div>

      {loadError && (
        <LoadError
          message={loadError}
          onRetry={() => { setLoading(true); void refresh(); }}
          className="mb-4"
        />
      )}

      {loading && <SkeletonLines lines={5} className="py-2" />}

      {!loading && loaded && sessions.length === 0 && (
        <EmptyState
          icon={Inbox}
          title={`No ${status.toLowerCase()} sessions`}
          description="Sessions appear here the moment a visitor opens your booking page — you'll see them filling the form in real time."
        />
      )}

      {/* One bordered list divided into rows, not N separate bordered cards: a
          list is one object, and the gaps between cards read as breaks in it.
          A real column header, because at 56px rows and this width the three
          fields are a table and were previously unlabelled. */}
      {sessions.length > 0 && (
        <div className={`${CARD} overflow-hidden`}>
          <div className="hidden grid-cols-[minmax(0,1fr)_14rem_11rem_auto] items-center gap-x-4 border-b border-gray-200 bg-gray-50 px-5 py-2.5 text-[13px] font-medium text-gray-500 sm:grid">
            <span className="min-w-0">Visitor</span>
            <span>Appointment</span>
            <span>Status</span>
            <span className="w-5" aria-hidden="true" />
          </div>

          <ul className="divide-y divide-gray-100">
            {sessions.map((s) => (
              <li key={s.id}>
                {/*
                  A grid rather than a flex row, so the same three cells can be
                  three columns on a laptop and a stack on a phone without
                  rendering any of them twice.

                  Below `sm` the appointment and the status were `hidden`
                  outright: a phone showed a name and an email - the two things
                  that identify a visitor, and nothing at all about the booking.
                  Hiding the answer is not a responsive strategy, and there is
                  no reason to reach for horizontal scrolling when the row can
                  simply reflow. The desktop layout is unchanged: the same
                  1fr / 14rem / 11rem / auto columns the header row declares.
                */}
                <Link
                  to={sessionDetailPath(pageId, s.id, status)}
                  className={`group grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-4 gap-y-1.5 px-5 py-4 transition-colors hover:bg-accent-50/60 sm:grid-cols-[minmax(0,1fr)_14rem_11rem_auto] ${FOCUS_RING}`}
                >
                  <span className="col-start-1 row-start-1 min-w-0">
                    <span className="block truncate text-[15px] font-medium text-gray-900">
                      {s.name || 'Anonymous visitor'}
                    </span>
                    <span className="block truncate text-[13px] text-gray-500">{s.email || 'no email yet'}</span>
                  </span>

                  <span className="col-start-1 row-start-2 truncate text-[13px] tabular-nums text-gray-600 sm:col-start-2 sm:row-start-1 sm:text-sm">
                    {s.selectedDate && s.selectedTime
                      ? formatCalendarDateTime(s.selectedDate, s.selectedTime)
                      : formatRelative(s.lastActivityAt)}
                  </span>

                  <span className="col-start-1 row-start-3 sm:col-start-3 sm:row-start-1">
                    <StatusBadge
                      status={s.status}
                      detail={s.status === 'Submitted' ? (isUpcoming(s, organizerTimeZone) ? 'Upcoming' : 'Completed') : undefined}
                    />
                  </span>

                  {/* The row is a link, and nothing else on it said so. The
                      chevron is the affordance; it darkens on hover so the
                      whole row reads as one target. Spans the stacked rows on a
                      phone so it stays centred against the whole block. */}
                  <ChevronRight
                    className="col-start-2 row-span-3 row-start-1 h-5 w-5 shrink-0 self-center text-gray-300 transition-colors group-hover:text-accent-600 sm:col-start-4 sm:row-span-1"
                    aria-hidden="true"
                  />
                </Link>
              </li>
            ))}
          </ul>
        </div>
      )}
    </>
  );
}
