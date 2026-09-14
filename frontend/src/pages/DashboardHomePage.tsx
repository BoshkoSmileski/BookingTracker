import { useCallback, useEffect, useState } from 'react';
import { Link, Navigate } from 'react-router-dom';
import { AlertTriangle, ExternalLink, Link2, Pencil } from 'lucide-react';
import {
  BUTTON_DANGER, BUTTON_GHOST, BUTTON_PRIMARY, CARD, ConfirmPanel, FOCUS_RING, InlineNotice,
  LoadError, META, PageHeader, SkeletonLines, StatusPill,
} from '../components/ui';
import { useAuth } from '../contexts/AuthContext';
import { api, ApiError, errorMessage as toErrorMessage } from '../lib/api';
import { hasBookableAvailability } from '../lib/availability';
import { COPIED_LABEL, useCopyToClipboard } from '../hooks/useCopyToClipboard';
import type { BookingPageSummaryDto } from '../lib/types';
import { useDocumentTitle } from '../lib/pageTitle';

/** The organizer's workspace: every booking page they own, with create/edit/enable-disable/delete/copy-link/preview actions. */
export function DashboardHomePage() {
  useDocumentTitle('Dashboard');
  const { callProtected } = useAuth();
  const [pages, setPages] = useState<BookingPageSummaryDto[] | null>(null);
  const { copied, copy } = useCopyToClipboard();
  const [errorId, setErrorId] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [confirmingDeleteId, setConfirmingDeleteId] = useState<string | null>(null);
  const [hasSchedule, setHasSchedule] = useState<boolean | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  /**
   * `pages` stays null on failure, which is what the skeleton keys off - so
   * without a `catch` this screen skeletoned for ever. It must also stay null
   * rather than becoming `[]`: an empty list here redirects to the create form
   * (below), so a transient API failure would have sent an organizer with
   * several booking pages to "Create a booking page".
   */
  const refresh = useCallback(() => {
    return callProtected((token) => api.organizer.getMyBookingPages(token))
      .then((result) => {
        setPages(result);
        setLoadError(null);
      })
      .catch((e) => setLoadError(toErrorMessage(e, 'Could not load your booking pages.')));
  }, [callProtected]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  useEffect(() => {
    callProtected((token) => api.availability.getSchedule(token))
      .then((schedule) => setHasSchedule(hasBookableAvailability(schedule)))
      // Deliberately quiet: this only decides whether the "set your working
      // hours" banner appears. Not knowing is a reason to say nothing, not to
      // report a second error beside the one the page list already shows.
      .catch(() => setHasSchedule(null));
  }, [callProtected]);

  // Keyed by the URL rather than by page id: the hook remembers the text it
  // copied, and slugs are unique, so one hook serves the whole grid without a
  // second identifier to keep in step.
  const bookingUrl = (page: BookingPageSummaryDto) => `${window.location.origin}/book/${page.slug}`;

  // Had no error handling at all, so a refused enable/disable looked exactly
  // like a button that does nothing - on a control whose whole purpose is to
  // decide whether the page is publicly bookable. Reported on the card it
  // belongs to, like the delete failure below.
  const handleToggleActive = async (page: BookingPageSummaryDto) => {
    setErrorId(null);
    setErrorMessage(null);
    try {
      await callProtected((token) => api.organizer.setBookingPageActive(token, page.id, !page.isActive));
      await refresh();
    } catch (err) {
      setErrorId(page.id);
      setErrorMessage(
        toErrorMessage(err, `Could not ${page.isActive ? 'disable' : 'enable'} this booking page.`),
      );
    }
  };

  const handleDelete = async (page: BookingPageSummaryDto) => {
    setConfirmingDeleteId(null);
    setErrorId(null);
    setErrorMessage(null);
    try {
      await callProtected((token) => api.organizer.deleteBookingPage(token, page.id));
      await refresh();
    } catch (err) {
      setErrorId(page.id);
      // The 409 is the interesting one ("...has confirmed bookings"), and
      // `errorMessage` already returns exactly that for any ApiError - so the
      // status check only remains as documentation of why the server's own
      // wording is worth showing here.
      setErrorMessage(
        err instanceof ApiError && err.status === 409
          ? err.problem.title
          : toErrorMessage(err, 'Failed to delete this booking page.'),
      );
    }
  };

  if (pages !== null && pages.length === 0) {
    return <Navigate to="/dashboard/new" replace />;
  }

  return (
    <>
      <PageHeader
        title="Booking pages"
        description="Each page is one thing guests can book, with its own duration, limits and instructions."
        actions={
          <Link to="/dashboard/new" className={BUTTON_PRIMARY}>
            New booking page
          </Link>
        }
      />

      {hasSchedule === false && pages && pages.length > 0 && (
        <div className="mb-6 flex flex-wrap items-center justify-between gap-4 rounded-lg border border-amber-200 bg-amber-50 px-5 py-4">
          <div className="flex items-start gap-3">
            <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-amber-600" aria-hidden="true" />
            <p className="text-[15px] text-amber-900">
              You haven&rsquo;t set your working hours yet &mdash; none of your booking pages can be booked until you do.
            </p>
          </div>
          <Link
            to={`/dashboard/${pages[0].id}/settings/hours`}
            className={`shrink-0 rounded-lg bg-amber-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-amber-700 ${FOCUS_RING}`}
          >
            Set working hours
          </Link>
        </div>
      )}

      {/*
        A workspace headline before the list.

        Derived entirely from the page list already fetched for the cards — no
        extra request, no new endpoint. It exists because the screen otherwise
        opens with nothing but a list, and the three things an organizer wants
        on arriving (how many pages, how many are live, how much is booked) were
        only obtainable by reading every card and adding up.
      */}
      {pages && pages.length > 0 && (
        // Stacked below sm: three 36px numerals across a 320px phone left each
        // cell about 90px, so "Upcoming bookings" wrapped to three lines under a
        // single digit. `divide-x` only where the row is actually a row.
        <dl className={`mb-6 grid grid-cols-1 ${CARD} divide-y divide-gray-200 sm:grid-cols-3 sm:divide-x sm:divide-y-0`}>
          <Figure label="Upcoming bookings" value={pages.reduce((n, p) => n + p.upcomingBookingCount, 0)} emphasis />
          <Figure label="Booking pages" value={pages.length} />
          <Figure label="Live" value={pages.filter((p) => p.isActive).length} hint="accepting bookings" />
        </dl>
      )}

      {/* Replaces the skeleton rather than sitting above it: with no pages
          loaded there is nothing else for the content area to show, and a
          skeleton left underneath would still be claiming to be loading. */}
      {pages === null && loadError && (
        <LoadError message={loadError} onRetry={() => void refresh()} />
      )}

      {pages === null && !loadError && <SkeletonLines lines={4} />}

      {/* Loaded once, then a reload failed - the cards below are real but may
          be stale, so they stay and this says so. */}
      {pages !== null && loadError && (
        <LoadError message={loadError} onRetry={() => void refresh()} className="mb-6" />
      )}

      {/*
        A grid of cards rather than full-width rows.

        A booking page is an object you pick, not a record you scan a column of,
        and as rows across a 1400px container a single page produced one bar with
        the title at the far left and its actions at the far right — the emptiest
        possible use of the width. Cards fill the row at any realistic count,
        keep each page's facts and actions together, and give the whole screen
        something to be.
      */}
      {pages && pages.length > 0 && (
        <ul className="grid gap-5 md:grid-cols-2 2xl:grid-cols-3">
          {pages.map((p) => (
            /* `min-w-0` for the same reason the analytics `Panel` carries it:
               a single-column `grid` track is sized `auto`, whose minimum is the
               item's content-based minimum, and the booking page title inside
               is `truncate`d - which makes its `min-content` the whole string
               rather than nothing. Without this the card was as wide as the
               longest title and the workspace scrolled sideways on a phone by
               however much that title overran, which is why it presented as an
               intermittent failure rather than a constant one. */
            <li key={p.id} className={`${CARD} flex min-w-0 flex-col p-5`}>
              <div className="flex items-start gap-4">
                {/* The upcoming-booking count is the one figure that differs
                    between pages and the reason to open one, so it leads. */}
                <span
                  className="flex h-16 w-16 shrink-0 flex-col items-center justify-center rounded-lg bg-accent-50 text-accent-700"
                  aria-hidden="true"
                >
                  <span className="text-2xl font-semibold tabular-nums leading-none">{p.upcomingBookingCount}</span>
                  <span className="mt-1 text-[10px] font-medium uppercase tracking-wide">upcoming</span>
                </span>

                <div className="min-w-0 flex-1">
                  <Link
                    to={`/dashboard/${p.id}`}
                    className={`block truncate rounded text-lg font-semibold tracking-tight text-gray-900 hover:text-accent-700 ${FOCUS_RING}`}
                    title={p.title}
                  >
                    {p.title}
                  </Link>
                  <p className={`mt-1 truncate ${META}`} title={`/${p.slug}`}>
                    /{p.slug}
                    <span aria-hidden="true" className="px-2 text-gray-300">·</span>
                    {p.durationMinutes} min
                  </p>
                  <div className="mt-2">
                    <StatusPill tone={p.isActive ? 'success' : 'neutral'}>
                      {p.isActive ? 'Active' : 'Disabled'}
                    </StatusPill>
                  </div>
                </div>
              </div>

              {errorId === p.id && errorMessage && (
                <InlineNotice tone="error" className="mt-4">{errorMessage}</InlineNotice>
              )}

              {/* Inside the card, so the page being deleted stays on screen and
                  named while the question is asked - which a native dialog
                  cannot do, and which matters most here because the cards are a
                  grid of near-identical objects. */}
              {confirmingDeleteId === p.id && (
                <div className="mt-4">
                  <ConfirmPanel
                    title={`Delete "${p.title}"?`}
                    confirmLabel="Delete page"
                    cancelLabel="Keep page"
                    onConfirm={() => void handleDelete(p)}
                    onCancel={() => setConfirmingDeleteId(null)}
                  >
                    <p>
                      Its public link stops working and its visitor sessions go with it. This cannot
                      be undone. A page with confirmed bookings cannot be deleted — disable it
                      instead.
                    </p>
                  </ConfirmPanel>
                </div>
              )}

              {/* Actions pinned to the card foot (`mt-auto`) so they line up
                  across a row of cards whose titles wrap to different heights.
                  The destructive one sits past a divider so it never reads as
                  one of the neutral four. */}
              <div className="mt-auto flex flex-wrap items-center gap-0.5 border-t border-gray-100 pt-4">
                <button type="button" onClick={() => void copy(bookingUrl(p))} className={BUTTON_GHOST}>
                  <Link2 className="h-4 w-4" aria-hidden="true" />
                  {copied === bookingUrl(p) ? COPIED_LABEL : 'Copy link'}
                </button>
                <a href={`/book/${p.slug}`} target="_blank" rel="noreferrer" className={BUTTON_GHOST}>
                  <ExternalLink className="h-4 w-4" aria-hidden="true" />
                  Preview
                </a>
                <Link to={`/dashboard/${p.id}/settings/details`} className={BUTTON_GHOST}>
                  <Pencil className="h-4 w-4" aria-hidden="true" />
                  Edit
                </Link>
                <button type="button" onClick={() => void handleToggleActive(p)} className={BUTTON_GHOST}>
                  {p.isActive ? 'Disable' : 'Enable'}
                </button>
                {/* No divider before Delete: in a card this narrow the row wraps,
                    and a separator stranded at the end of a line reads as a
                    rendering fault. The red already separates it. */}
                <button
                  type="button"
                  onClick={() => setConfirmingDeleteId((current) => (current === p.id ? null : p.id))}
                  aria-expanded={confirmingDeleteId === p.id}
                  className={BUTTON_DANGER}
                >
                  Delete
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}

/** One headline figure. Mirrors the per-page dashboard's band, so the two screens read alike. */
function Figure({
  label,
  value,
  hint,
  emphasis,
}: {
  label: string;
  value: number;
  hint?: string;
  emphasis?: boolean;
}) {
  return (
    <div className="px-5 py-4">
      <dt className="text-[13px] font-medium text-gray-500">{label}</dt>
      <dd
        className={`mt-1 font-semibold tabular-nums tracking-tight ${emphasis ? 'text-4xl' : 'text-3xl'} ${
          value === 0 ? 'text-gray-500' : emphasis ? 'text-accent-700' : 'text-gray-900'
        }`}
      >
        {value}
      </dd>
      {hint && <p className="mt-0.5 text-[13px] text-gray-500">{hint}</p>}
    </div>
  );
}
