import type { ReactNode } from 'react';
import { MAIN_CONTENT_ID, SkipLink } from '../ui';

/**
 * The surface every guest-facing screen sits on: the booking wizard and the
 * three "manage my booking" pages.
 *
 * It exists because those four screens each declared their own
 * `min-h-screen bg-gray-* flex items-center justify-center` wrapper with its
 * own width and its own radius, so the one journey a guest takes - book, then
 * come back to manage - visibly changed surface halfway through. The wizard had
 * already been brought onto `shell-100` and `rounded-lg`; the other three were
 * still on `gray-50` and `rounded-2xl`, which is the same gap the fourth UI
 * pass found between the organizer app and the wizard, one screen further out.
 *
 * `shell-100` rather than `gray-50` for the reason the palette records: a
 * cool-gray page behind a white card is the most recognisable centred-form
 * template surface there is, and this is the one screen an organizer's own
 * customers see.
 *
 * **Top-aligned, not vertically centred.** Centring is only ever right for
 * content shorter than the viewport, and the booking wizard is not - a centred
 * calendar pushes its own time list below the fold on a laptop and jumps
 * upward every time the content grows. Top alignment keeps the header in the
 * same place on every step, which is what makes it read as one page rather
 * than a sequence of dialogs.
 *
 * **Edge to edge below `sm`.** A phone has no room to spend 24px of margin on
 * either side of a seven-column calendar; dropping the horizontal padding and
 * the side borders is worth roughly 4px per calendar cell, which is the
 * difference between a comfortable target and a cramped one at 320px.
 */
export function PublicShell({ children }: { children: ReactNode }) {
  return (
    <div className="min-h-screen bg-shell-100">
      <SkipLink />
      {/*
        896px, one measure for every step so the header never changes width
        underneath the guest. Wide enough that the calendar and the time list
        sit side by side with real room - at 768px the calendar cells were 41px
        and the details form ran two 316px columns - and far short of the
        1440px the organizer app uses, because this is a sheet to fill in, not
        a workspace.
      */}
      <div className="mx-auto w-full max-w-4xl sm:px-6 sm:py-10">
        {/*
          The guest flow had no `main` landmark at all, so a screen-reader user
          jumping by landmark found nothing to jump to on the one screen an
          organizer's own customers use. It is the white sheet itself rather
          than a wrapper around it, so the visual layout is byte-for-byte
          unchanged.

          `tabIndex={-1}` is what makes the skip link actually move focus:
          browsers do not focus a plain `<main>` on a same-document jump, so
          without it the link scrolls and leaves focus where it was.
        */}
        <main
          id={MAIN_CONTENT_ID}
          tabIndex={-1}
          className="border-y border-gray-200 bg-white p-5 shadow-sm focus:outline-none sm:rounded-lg sm:border-x sm:p-8"
        >
          {children}
        </main>
      </div>
    </div>
  );
}

/**
 * A whole-screen state - not found, unreachable API - on the same surface.
 *
 * Separate from `PublicShell` so these do not inherit the booking sheet's
 * measure: a one-line message in a 768px column is the emptiest possible use
 * of the space, and this is the one guest-facing case where centring is right,
 * because the content genuinely is shorter than the viewport.
 */
export function PublicMessage({
  title,
  children,
}: {
  title: string;
  children?: ReactNode;
}) {
  return (
    <div className="flex min-h-screen items-center justify-center bg-shell-100 px-4 py-10">
      {/* A landmark here too: this is a whole screen, not a fragment, and it is
          what a guest following a bad link actually lands on. No skip link -
          there is nothing to skip past. */}
      <main
        id={MAIN_CONTENT_ID}
        className="w-full max-w-md rounded-lg border border-gray-200 bg-white p-8 text-center shadow-sm"
      >
        <h1 className="text-lg font-semibold text-gray-900">{title}</h1>
        {children && <div className="mt-2 text-sm leading-relaxed text-gray-500">{children}</div>}
      </main>
    </div>
  );
}
