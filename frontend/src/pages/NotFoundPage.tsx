import { Compass } from 'lucide-react';
import { Link, useNavigate } from 'react-router-dom';
import { PublicMessage } from '../components/public/PublicShell';
import { BUTTON_PRIMARY, BUTTON_SECONDARY, EmptyState, PageHeader } from '../components/ui';
import { useDocumentTitle } from '../lib/pageTitle';

/**
 * The two shapes of "that address does not exist".
 *
 * `App.tsx` previously had no catch-all at all, so an unmatched path rendered
 * `null` - a completely white page with no header, no rail and no link out,
 * which is indistinguishable from a crash and leaves the URL bar as the only
 * way back. Both screens below exist to make that impossible.
 *
 * Two of them rather than one because the app has two surfaces and they are not
 * interchangeable: a signed-in organizer should keep the rail they were using,
 * and a guest who mistyped a booking link should land on the same sheet the
 * booking pages use rather than being shown an application chrome they have no
 * account for. Neither invents a treatment - the guest one is the existing
 * `PublicMessage`, the organizer one is `PageHeader` + `EmptyState`.
 */

/** Unauthenticated, whole-screen. Reached by anything outside `/dashboard`. */
export function NotFoundPage() {
  useDocumentTitle('Page not found');

  return (
    <PublicMessage title="Page not found">
      <p>The address you followed does not exist. It may have been mistyped, or the link may be out of date.</p>
      <div className="mt-5 flex justify-center">
        <Link to="/" className={BUTTON_PRIMARY}>
          Go to the home page
        </Link>
      </div>
    </PublicMessage>
  );
}

/**
 * Inside the organizer shell, so the rail, the booking page list and the way
 * back to every other screen all stay exactly where they were. Mounted at
 * `/dashboard/*`, which ranks below every real dashboard route and above the
 * public catch-all.
 */
export function DashboardNotFoundPage() {
  useDocumentTitle('Page not found');
  const navigate = useNavigate();

  return (
    <>
      <PageHeader title="Page not found" />
      <EmptyState
        icon={Compass}
        title="This page doesn’t exist"
        description="The address may have been mistyped, or it may point at a booking page that has since been deleted."
        action={
          <div className="flex flex-wrap justify-center gap-2">
            <Link to="/dashboard" className={BUTTON_PRIMARY}>
              Back to booking pages
            </Link>
            {/* -1 rather than a second Link: whatever the organizer was looking
                at before is a better destination than anything this screen can
                guess, and it is the one route out that survives a deleted page. */}
            <button type="button" onClick={() => navigate(-1)} className={BUTTON_SECONDARY}>
              Go back
            </button>
          </div>
        }
      />
    </>
  );
}
