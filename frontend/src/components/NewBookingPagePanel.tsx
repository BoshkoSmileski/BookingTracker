import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { AlertTriangle, CheckCircle2, ExternalLink, Link2 } from 'lucide-react';
import { useAuth } from '../contexts/AuthContext';
import { api } from '../lib/api';
import { hasBookableAvailability } from '../lib/availability';
import { COPIED_LABEL, useCopyToClipboard } from '../hooks/useCopyToClipboard';
import { BUTTON_PRIMARY, BUTTON_SECONDARY, CARD, FOCUS_RING, META } from './ui';

/**
 * What to do next, immediately after a booking page is created.
 *
 * Creating a page used to land the organizer on this screen's session list,
 * which for a page nobody has ever visited is an empty state reading "No active
 * sessions" - correct, and a dead end. The one thing they need at that moment,
 * the public link, was not on the screen at all: it lived on the workspace list
 * they had just navigated away from.
 *
 * Shown only for the page just created (router state from the create form), so
 * nothing changes on a screen an organizer returns to later.
 */
export function NewBookingPagePanel({ pageId, slug }: { pageId: string; slug: string }) {
  const { callProtected } = useAuth();
  const [bookable, setBookable] = useState<boolean | null>(null);
  const { copied, copy } = useCopyToClipboard();

  useEffect(() => {
    callProtected((token) => api.availability.getSchedule(token))
      .then((schedule) => setBookable(hasBookableAvailability(schedule)))
      // Left as `null`, which renders nothing - deliberately, and for the same
      // reason the loading state does: this panel's entire job is to state which
      // of two things is true, and it must not guess when it does not know.
      // The page it sits on reports the failure itself.
      .catch(() => setBookable(null));
  }, [callProtected]);

  const publicUrl = `${window.location.origin}/book/${slug}`;

  // Availability decides which panel this is, so nothing is claimed before the
  // answer arrives - "your page is live" beside hours that turn out not to
  // exist is worse than a moment of nothing.
  if (bookable === null) return null;

  if (!bookable) {
    return (
      <section className={`${CARD} mb-8 border-amber-200 bg-amber-50 p-5`} aria-label="Booking page created">
        <div className="flex items-start gap-3">
          <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-amber-600" aria-hidden="true" />
          <div className="min-w-0">
            <h2 className="text-base font-semibold text-amber-900">Your booking page is created</h2>
            <p className="mt-1 text-sm text-amber-900">
              There are no working hours to book yet, so guests opening this page will find nothing
              available.
            </p>
            {/* The notice on the hours screen keys off this state, so the
                organizer arrives there with the same sentence still in view. */}
            <Link
              to={`/dashboard/${pageId}/settings/hours`}
              state={{ justCreatedPage: true }}
              className={`${BUTTON_PRIMARY} mt-4`}
            >
              Set working hours
            </Link>
          </div>
        </div>
      </section>
    );
  }

  return (
    <section className={`${CARD} mb-8 p-5`} aria-label="Booking page created">
      <div className="flex items-start gap-3">
        <CheckCircle2 className="mt-0.5 h-5 w-5 shrink-0 text-accent-600" aria-hidden="true" />
        <div className="min-w-0 flex-1">
          <h2 className="text-base font-semibold text-gray-900">Your booking page is live</h2>
          <p className="mt-1 text-sm text-gray-500">
            It already uses your working hours. Share the link and bookings will appear on this
            screen as they happen.
          </p>

          <div className="mt-4 flex flex-wrap items-center gap-2">
            <code className="min-w-0 flex-1 truncate rounded-lg bg-gray-50 px-3 py-2 text-[13px] text-gray-700 ring-1 ring-gray-200">
              {publicUrl}
            </code>
            <button type="button" onClick={() => void copy(publicUrl)} className={BUTTON_PRIMARY}>
              <Link2 className="h-4 w-4" aria-hidden="true" />
              {copied === publicUrl ? COPIED_LABEL : 'Copy link'}
            </button>
            <a href={`/book/${slug}`} target="_blank" rel="noreferrer" className={BUTTON_SECONDARY}>
              <ExternalLink className="h-4 w-4" aria-hidden="true" />
              Preview
            </a>
          </div>

          {/* Three settings a new page most often wants, as links rather than
              steps: none of them is required, and numbering them would imply
              the page is unfinished when it is not. */}
          <p className={`mt-4 ${META}`}>
            Fine-tune it:{' '}
            <NextLink to={`/dashboard/${pageId}/settings/hours`}>Working hours</NextLink>
            <Divider />
            <NextLink to={`/dashboard/${pageId}/settings/meeting`}>Meeting type</NextLink>
            <Divider />
            <NextLink to={`/dashboard/${pageId}/settings/instructions`}>Booking instructions</NextLink>
          </p>
        </div>
      </div>
    </section>
  );
}

function NextLink({ to, children }: { to: string; children: string }) {
  return (
    <Link to={to} className={`rounded font-medium text-accent-700 hover:underline ${FOCUS_RING}`}>
      {children}
    </Link>
  );
}

function Divider() {
  return <span aria-hidden="true" className="px-2 text-gray-400">·</span>;
}
