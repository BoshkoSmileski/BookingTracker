import { Link } from 'react-router-dom';
import { CalendarCheck } from 'lucide-react';
import { CARD, FOCUS_RING, MAIN_CONTENT_ID } from './ui';
import type { ReactNode } from 'react';

/**
 * The surface `/login` and `/register` sit on.
 *
 * Both pages were the last two screens still on the pre-accent design - a
 * `rounded-2xl` card on `bg-gray-50`, `bg-gray-900` buttons, a grey focus ring
 * and their own `inputClass`/`labelClass` copies. Every other public screen had
 * been brought onto `PublicShell`; these were missed for the same reason the
 * booking wizard was, which is that nothing imports a design system on your
 * behalf.
 *
 * Not `PublicShell` itself: that is an 896px sheet for the booking wizard and
 * the manage screens, and a two-field sign-in form in an 896px column is the
 * emptiest possible use of it. This is the `PublicMessage` shape - a narrow
 * centred card on the same warm `shell-100` - which is right here for the
 * reason centring is usually wrong: the content genuinely is shorter than the
 * viewport.
 *
 * The wordmark links home, so an unauthenticated visitor who arrived at a
 * protected URL and does not have an account has somewhere to go that is not
 * the form in front of them.
 */
export function AuthShell({
  title,
  description,
  children,
  footer,
}: {
  title: string;
  description?: ReactNode;
  children: ReactNode;
  footer?: ReactNode;
}) {
  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-shell-100 px-4 py-10">
      <Link
        to="/"
        className={`mb-6 inline-flex items-center gap-2 rounded text-[15px] font-semibold tracking-tight text-gray-900 ${FOCUS_RING}`}
      >
        <CalendarCheck className="h-5 w-5 text-accent-600" aria-hidden="true" />
        BookingTracker
      </Link>

      {/* The card is the landmark. One wordmark link sits above it, which is
          not enough navigation to warrant a skip link - but a screen-reader
          user still needs somewhere to jump to. */}
      <main id={MAIN_CONTENT_ID} className={`${CARD} w-full max-w-sm p-8 shadow-sm`}>
        <h1 className="text-2xl font-semibold tracking-tight text-gray-900">{title}</h1>
        {description && <p className="mt-1.5 text-[15px] leading-relaxed text-gray-500">{description}</p>}

        <div className="mt-6">{children}</div>
      </main>

      {footer && <p className="mt-5 text-sm text-gray-500">{footer}</p>}
    </div>
  );
}
