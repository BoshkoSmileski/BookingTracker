import { Link } from 'react-router-dom';
import { ArrowRight, CalendarCheck, Mail, Video } from 'lucide-react';
import { BUTTON_PRIMARY, BUTTON_SECONDARY, FOCUS_RING, MAIN_CONTENT_ID, SkipLink } from '../components/ui';
import { useAuth } from '../contexts/AuthContext';
import { DEMO_BOOKING_PAGE_SLUG } from '../lib/devDemo';
import type { ComponentType } from 'react';
import { useDocumentTitle } from '../lib/pageTitle';

const DEMO_SLUG = DEMO_BOOKING_PAGE_SLUG;

/**
 * The front door.
 *
 * This was three links in a 384px card - "Open booking page", "Organizer
 * dashboard" - on gray-50, with near-black buttons and a 2xl radius: the
 * pre-design-system look the rest of the app has since left behind, and a page
 * that never said what the product was or who it was for.
 *
 * Deliberately not a marketing site. No hero gradient, no floating feature
 * cards, no illustration: this is a scheduling tool, and the fastest way to
 * look like a real one is to state plainly what happens, in the app's own type
 * scale and its own accent. One primary action, one secondary, and the demo
 * kept - it is the quickest honest answer to "what does this actually look
 * like", and it was the only thing the old page offered.
 */
export function HomePage() {
  useDocumentTitle(null);
  const { isAuthenticated } = useAuth();

  return (
    <div className="min-h-screen bg-shell-50 text-gray-900">
      <SkipLink />
      <header className="mx-auto flex w-full max-w-5xl items-center justify-between gap-4 px-6 py-5">
        <span className="flex items-center gap-2 text-[15px] font-semibold tracking-tight">
          <CalendarCheck className="h-5 w-5 text-accent-600" aria-hidden="true" />
          BookingTracker
        </span>

        {/* Signed in is not a marketing audience - the useful action becomes the
            one they came back for. */}
        {isAuthenticated ? (
          <Link to="/dashboard" className={BUTTON_PRIMARY}>
            Go to dashboard
          </Link>
        ) : (
          <nav className="flex items-center gap-2" aria-label="Account">
            <Link to="/login" className={BUTTON_SECONDARY}>Sign in</Link>
            <Link to="/register" className={BUTTON_PRIMARY}>Create account</Link>
          </nav>
        )}
      </header>

      <main id={MAIN_CONTENT_ID} tabIndex={-1} className="mx-auto w-full max-w-5xl px-6 pb-20 focus:outline-none">
        <section className="max-w-2xl pt-12 sm:pt-20">
          <h1 className="text-4xl font-semibold leading-tight tracking-tight sm:text-5xl">
            Let people book your time — and see exactly how they did it.
          </h1>
          <p className="mt-5 text-lg leading-relaxed text-gray-600">
            BookingTracker gives you a public booking page backed by your real calendar, and an
            organizer dashboard that records every step a visitor takes on it, as it happens.
          </p>
          <p className="mt-3 text-[15px] leading-relaxed text-gray-500">
            For consultants, tutors, advisors and anyone who schedules one-to-one meetings and would
            rather not trade six emails to find a time.
          </p>

          {!isAuthenticated && (
            <div className="mt-8 flex flex-wrap items-center gap-3">
              <Link to="/register" className={`${BUTTON_PRIMARY} px-5 py-2.5 text-[15px]`}>
                Create an organizer account
                <ArrowRight className="h-4 w-4" aria-hidden="true" />
              </Link>
              <Link to="/login" className={`${BUTTON_SECONDARY} px-5 py-2.5 text-[15px]`}>
                Sign in
              </Link>
            </div>
          )}
        </section>

        {/* Numbered, because getting started genuinely is a sequence and there
            are only three of them. A list, not three cards - the content is one
            sentence each and a border around each would be packaging. */}
        <section className="mt-16 border-t border-shell-200 pt-10" aria-labelledby="how-heading">
          <h2 id="how-heading" className="text-base font-semibold tracking-tight">
            Getting started
          </h2>
          <ol className="mt-5 grid gap-6 sm:grid-cols-3">
            <Step
              n={1}
              title="Set your hours"
              body="New accounts start on Monday to Friday, 09:00–17:00, in your own time zone. Adjust it whenever you like."
            />
            <Step
              n={2}
              title="Share your link"
              body="Each booking page has its own address. Guests pick a time and book — no account, no sign-up."
            />
            <Step
              n={3}
              title="Watch it arrive"
              body="Confirmations, calendar invitations and reminders go out on their own. You get the bookings and the numbers behind them."
            />
          </ol>
        </section>

        <section className="mt-14 border-t border-shell-200 pt-10" aria-labelledby="what-heading">
          <h2 id="what-heading" className="text-base font-semibold tracking-tight">
            What it does for you
          </h2>
          <ul className="mt-5 grid gap-x-10 gap-y-5 sm:grid-cols-2">
            <Capability icon={CalendarCheck} title="Your calendar decides what is free">
              Connect Google Calendar and your existing meetings block booking slots automatically.
              Confirmed bookings appear on it, and move or disappear when they do.
            </Capability>
            <Capability icon={Video} title="Online meetings without the extra step">
              Set a page to Google Meet and every booking gets a real join link — in the
              confirmation, the calendar invitation and the reminders.
            </Capability>
            <Capability icon={Mail} title="The follow-up handles itself">
              Confirmations, cancellations, reschedules and as many reminders as you want, each with
              a calendar invitation that updates in place.
            </Capability>
            <Capability icon={ArrowRight} title="Every booking, start to finish">
              Each visit is recorded step by step, so you can see where people hesitate, where they
              leave, and how long booking actually takes.
            </Capability>
          </ul>
        </section>

        {/* Kept from the old page, and moved to the end where it belongs: the
            demo is what someone reaches for after deciding to look, not
            before. */}
        <section
          className="mt-14 rounded-lg border border-shell-200 bg-white px-6 py-5"
          aria-labelledby="demo-heading"
        >
          <h2 id="demo-heading" className="text-[15px] font-semibold tracking-tight">
            Just looking?
          </h2>
          <p className="mt-1.5 text-sm leading-relaxed text-gray-600">
            Open the{' '}
            <Link to={`/book/${DEMO_SLUG}`} className={`rounded font-medium text-accent-700 underline ${FOCUS_RING}`}>
              demo booking page
            </Link>{' '}
            to book a time exactly as a guest would — no account needed.
            {/* The seeded organizer's password used to be printed here in every
                build, which published a working credential. The demo booking
                page is public and stays; signing in as the demo organizer is a
                development convenience and lives behind the same gate the login
                form uses. */}
            {import.meta.env.DEV && (
              <>
                {' '}To see the other side of it, use <strong className="font-medium">Fill demo credentials</strong> on the{' '}
                <Link to="/login" className={`rounded font-medium text-accent-700 underline ${FOCUS_RING}`}>
                  sign-in page
                </Link>
                .
              </>
            )}
          </p>
        </section>
      </main>
    </div>
  );
}

function Step({ n, title, body }: { n: number; title: string; body: string }) {
  return (
    <li>
      <span
        className="flex h-7 w-7 items-center justify-center rounded-full bg-accent-600 text-[13px] font-semibold text-white"
        aria-hidden="true"
      >
        {n}
      </span>
      <h3 className="mt-3 text-[15px] font-medium">{title}</h3>
      <p className="mt-1 text-sm leading-relaxed text-gray-600">{body}</p>
    </li>
  );
}

function Capability({
  icon: IconComponent,
  title,
  children,
}: {
  icon: ComponentType<{ className?: string }>;
  title: string;
  children: string;
}) {
  return (
    <li className="flex gap-3">
      <IconComponent className="mt-0.5 h-5 w-5 shrink-0 text-accent-600" aria-hidden="true" />
      <div className="min-w-0">
        <h3 className="text-[15px] font-medium">{title}</h3>
        <p className="mt-1 text-sm leading-relaxed text-gray-600">{children}</p>
      </div>
    </li>
  );
}
