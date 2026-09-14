import { useId } from 'react';
import type { ComponentType, ReactNode } from 'react';

/**
 * The design system.
 *
 * Class constants and small components — not a component library. Pages write
 * Tailwind directly (per Coding Conventions); this exists so that card radius,
 * control height, type scale and feedback treatment have one definition each
 * instead of one per page.
 *
 * Sized for a desktop application on a large monitor, which is what this is:
 *
 *   type       24 page title / 16 section heading / 15 body / 13 meta
 *   controls   38-40px tall — a comfortable pointer target, not a compact one
 *   rows       56px table rows, so twenty of them can be read down a column
 *   radius     rounded-lg (8px); large enough to feel considered, far short of
 *              the pill-shaped cards that read as consumer software
 *   colour     greyscale, plus one accent (deep pine-teal) carrying primary
 *              actions, active navigation, selection and focus. See index.css.
 *
 * An earlier pass sized all of this ~25% smaller. It was legible but read as a
 * compact utility panel rather than an application, so the scale here is
 * deliberate rather than default.
 */

/** A discrete object: a list, a row, a stat, a panel. Not a section wrapper. */
export const CARD = 'rounded-lg border border-gray-200 bg-white';

/**
 * Keyboard focus, in the accent rather than in near-black: a gray-900 ring is
 * invisible against the dark buttons it most needs to appear on.
 */
export const FOCUS_RING =
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent-500 focus-visible:ring-offset-2';

const BUTTON_BASE =
  `inline-flex items-center justify-center gap-2 rounded-lg px-4 py-2 text-sm font-medium transition-colors ${FOCUS_RING} disabled:cursor-not-allowed`;

/** The accent carries the primary action, so the one coloured thing on a screen is the thing to do next. */
export const BUTTON_PRIMARY =
  `${BUTTON_BASE} bg-accent-600 text-white hover:bg-accent-700 disabled:bg-gray-300`;

export const BUTTON_SECONDARY =
  `${BUTTON_BASE} border border-gray-300 bg-white text-gray-700 hover:border-gray-400 hover:bg-gray-50 disabled:text-gray-400`;

/** Destructive actions read as destructive on hover, not at rest — they sit in ordinary rows. */
export const BUTTON_DANGER =
  `${BUTTON_BASE} border border-transparent text-red-600 hover:border-red-200 hover:bg-red-50 disabled:text-red-300`;

/** A destructive action already chosen and now being confirmed. Rare by design. */
export const BUTTON_DANGER_SOLID = `${BUTTON_BASE} bg-red-600 text-white hover:bg-red-700 disabled:bg-gray-300`;

/** Dense inline actions inside a row or panel header, where a bordered button would shout. */
export const BUTTON_GHOST = `${BUTTON_BASE} text-gray-600 hover:bg-gray-100 hover:text-gray-900`;

/**
 * Inputs share the button ring, so keyboard focus looks identical on every
 * control — two focus treatments in one form read as two kinds of control.
 */
export const INPUT =
  `rounded-lg border border-gray-300 bg-white px-3 py-2 text-[15px] text-gray-900 transition-colors placeholder:text-gray-400 hover:border-gray-400 focus:border-accent-500 ${FOCUS_RING}`;

export const LABEL = 'block text-sm font-medium text-gray-700';

/**
 * The gap between a form label and the control it names.
 *
 * `LABEL` is the type; this is the rhythm. They are separate constants because
 * a handful of labels legitimately want neither — `CreateBookingPagePage` pairs
 * each label with a helper line that supplies its own `mt-1`, and baking a
 * margin in there would either double the gap or force a `mb-0` override, which
 * is exactly the Tailwind precedence trap this codebase has already been caught
 * by once (stylesheet order decides, not attribute order).
 *
 * The app previously split almost evenly between `mb-1.5` (14 sites) and `mb-1`
 * (12), so two settings screens reached from the same nav group sat 2px apart.
 * 1.5 won because it is what the guest wizard and the largest forms already
 * used.
 */
export const LABEL_GAP = 'mb-1.5';

/**
 * A form label, with the one way this app says a field is optional.
 *
 * Required-by-default, stated in words on the exceptions rather than by an
 * asterisk and a legend the user has to go and find: "(optional)" on the few
 * optional fields is the shorter list and the one that actually removes work.
 *
 * This lived in `DetailsStep` and was the only correct implementation of it.
 * Everywhere else spelled the marker by hand, five different ways — `(optional)`
 * styled, `(Optional)` with a leading space, and three variants that folded
 * domain context into the parenthesis (`(optional, shared with the guest)`).
 * Those explanations are worth keeping, but they are *help text*, not part of
 * the marker: they belong beside the control with an `aria-describedby`, so the
 * marker itself can be one string everywhere.
 *
 * Deliberately just the label. The larger `<Field>` abstraction — label +
 * control + hint + error in one component — is a separate piece of work, and
 * wrapping every input in this codebase is not something to do as a side effect
 * of standardising a marker.
 */
export function FieldLabel({
  htmlFor,
  children,
  optional = false,
  className = '',
}: {
  htmlFor: string;
  children: ReactNode;
  optional?: boolean;
  /** Appended last. For position or colour — not for overriding the gap. */
  className?: string;
}) {
  return (
    <label className={`${LABEL} ${LABEL_GAP} ${className}`} htmlFor={htmlFor}>
      {children}
      {/* A real space, not just a margin: the accessible name is built from
          text content, so `Topic` + `(optional)` with only CSS between them
          announces as "Topic(optional)". */}
      {optional && <> <span className="font-normal text-gray-500">(optional)</span></>}
    </label>
  );
}

/**
 * The gap between a control and the hint or error underneath it — `LABEL_GAP`'s
 * counterpart on the other side of the input.
 *
 * Same drift, same fix. The app spelled this `mt-1` in three places
 * (`WorkingHoursPage`'s time zone hint *and* its error, `RegisterPage`'s
 * password hint) and `mt-1.5` in two more (`DetailsStep`, `CancelBookingPage`),
 * so the same sentence sat 2px apart depending on which screen it was on. On
 * `WorkingHoursPage` that mattered twice over, because there the hint and the
 * error are one element in two states — any disagreement between them is a
 * layout shift at the moment a save is rejected. 1.5 wins for the reason it won
 * above: it mirrors `LABEL_GAP`, so a field is symmetrical about its control.
 */
export const HELP_GAP = 'mt-1.5';

/**
 * The wiring a control needs to be joined to its own label, hint and error.
 * Spread it onto whichever element is the actual control.
 *
 * `undefined` rather than `false`/`''` on both ARIA attributes: React omits an
 * undefined attribute entirely, and `aria-invalid="false"` on every untouched
 * input is noise that says nothing a missing attribute does not already say.
 */
export interface FieldControlProps {
  id: string;
  'aria-describedby': string | undefined;
  'aria-invalid': true | undefined;
}

/**
 * A form field: label, control, and the one line of help or failure under it.
 *
 * `FieldLabel` standardised the *label* and deliberately stopped there, noting
 * that the larger wrapper was a separate piece of work. This is that work, and
 * it is deliberately still small — it owns four things and nothing else:
 *
 *   1. the label, via `FieldLabel`, so there is no second label styling system
 *   2. the "(optional)" marker, which `FieldLabel` already made one string
 *   3. the hint/error line, and the gap above it (`HELP_GAP`)
 *   4. the ARIA that joins 1 and 3 to the control
 *
 * The fourth is the reason it exists. That wiring was hand-written at every
 * site, so it was correct where somebody had thought about it and absent where
 * nobody had: three settings pages re-declared `` `${LABEL} ${LABEL_GAP}` ``
 * locally rather than using `FieldLabel` at all, and the hint under
 * `CalendarIntegrationPage`'s title format was `text-xs` — 12px, off the type
 * scale — where every other hint is `META`'s 13px. One field, one id scheme,
 * one place to get it right.
 *
 * ### Why the child is a function
 *
 * The obvious alternative is `cloneElement`, which would let a caller write
 * `<Field label="…"><input value={…} /></Field>`. It is rejected because it is
 * wrong exactly where it would be trusted most: several controls in this app
 * are not a single element. `CalendarIntegrationPage`'s reminder field is an
 * `<input>` beside a "minutes before" `<span>` inside a flex `<div>`, so a
 * clone would put the `id` and the ARIA on the **div** — a label pointing at a
 * non-control, silently, with nothing on screen to show for it.
 * `DetailsStep` branches between a `<textarea>` and an `<input>`, and
 * `title-format` is a `<select>` plus a conditionally rendered box where the
 * hint describes the second one. A render prop hands the caller the wiring and
 * lets them put it on the element that is genuinely the control, which is the
 * only party that knows which one that is.
 *
 * ### Error replaces hint
 *
 * `WorkingHoursPage` established this and it is preserved verbatim: when there
 * is an error the hint is not rendered, and `aria-describedby` names the error.
 * The two are alternatives rather than a stack — "An IANA identifier, e.g.
 * Europe/Skopje" underneath "'Europe/Skopj' is not a recognized IANA time zone
 * id." is the same sentence twice, and reading both out is worse than reading
 * the useful one.
 *
 * And with neither, `aria-describedby` is **absent**, not empty: an IDREF
 * pointing at nothing is a broken reference, and some screen readers announce
 * the failure rather than ignoring it.
 *
 * Ids come from `useId`, as `Section` and `ConfirmPanel` already do, so two
 * fields with the same label on one screen cannot collide — the hand-written
 * ids this replaced were unique only by inspection.
 */
export function Field({
  label,
  hint,
  error,
  optional = false,
  className = '',
  children,
}: {
  label: ReactNode;
  /** Standing help. Rendered only when there is no `error`. */
  hint?: ReactNode;
  /** A rejection for this specific field — usually `validationErrors(e).for(key)[0]`. */
  error?: ReactNode;
  optional?: boolean;
  /** On the field's wrapper. For width or grid placement, not for spacing inside it. */
  className?: string;
  children: (control: FieldControlProps) => ReactNode;
}) {
  const base = useId();
  const controlId = `${base}control`;
  const hintId = `${base}hint`;
  const errorId = `${base}error`;

  // A ReactNode is only an error if there is something to say: `error={undefined}`
  // and `error={''}` are both "no error", which is what `.for(key)[0]` returns
  // for a field the server did not complain about.
  const hasError = Boolean(error);
  const hasHint = !hasError && Boolean(hint);

  return (
    <div className={className}>
      <FieldLabel htmlFor={controlId} optional={optional}>
        {label}
      </FieldLabel>
      {children({
        id: controlId,
        'aria-describedby': hasError ? errorId : hasHint ? hintId : undefined,
        'aria-invalid': hasError ? true : undefined,
      })}
      {hasError ? (
        <p id={errorId} className={`${HELP_GAP} text-[13px] text-red-600`}>
          {error}
        </p>
      ) : hasHint ? (
        <p id={hintId} className={`${HELP_GAP} ${META}`}>
          {hint}
        </p>
      ) : null}
    </div>
  );
}

/**
 * A checkbox at the scale of the rest of the app.
 *
 * These were the only unstyled control left: a native checkbox renders at about
 * 13px, which beside a 40px input and 15px text reads as unfinished, and it
 * draws itself in the OS accent — Windows blue — putting a second accent colour
 * on four settings screens. `accent-color` in index.css fixes the colour for
 * every checkbox at once; this fixes the size and gives it the same focus ring
 * as every other control, so a keyboard user sees one treatment throughout.
 *
 * Still a native input, deliberately: a div-based checkbox would have to
 * re-implement the label association, the space key, the indeterminate state
 * and the form semantics that already work here.
 */
export const CHECKBOX =
  `h-[18px] w-[18px] shrink-0 cursor-pointer rounded border-gray-300 ${FOCUS_RING}`;

/** A heading for a group inside a page — one step below the page title. */
export const SECTION_HEADING = 'text-base font-semibold tracking-tight text-gray-900';

/**
 * Secondary text: hints, timestamps, counts.
 *
 * gray-500 is the quietest this app goes for anything a user is meant to read.
 * There used to be a third, quieter tier — gray-400 for hints and gray-300 for
 * zeroed figures — spread across sixty-odd places. On white those measure about
 * 2.9:1 and 1.6:1, so they failed WCAG AA outright, and the practical effect was
 * worse than the audit implies: the app had two "quiet" greys one step apart,
 * which is not a hierarchy, just blur. Everything a user reads is now gray-900,
 * gray-700 or this.
 *
 * gray-400 survives only for genuinely non-textual marks — separator dots,
 * decorative icons, placeholder text — where nothing is lost if it goes unread.
 */
export const META = 'text-[13px] text-gray-500';

/**
 * The same quiet tier at body size, for a sentence rather than a caption —
 * an empty-list line, a disabled state, a "nothing here" note.
 */
export const MUTED = 'text-sm text-gray-500';

/**
 * The measure for an editing screen, centred.
 *
 * Settings pages used to be left-aligned at 576-672px inside a 1024px column
 * inside a ~1900px window, so a form sat in the left third of the screen with
 * the rest blank. A centred 768px column is a document to edit rather than a
 * panel pushed into a corner, and it is the same width on every settings screen
 * — those widths previously varied (xl / 2xl / 3xl) between siblings reached
 * from the same nav group, so the content jumped as you moved between them.
 */
export const FORM_COLUMN = 'mx-auto w-full max-w-3xl';

/**
 * The id every shell puts on its `<main>`, and the only thing `SkipLink`
 * targets. One constant so the link and its destination cannot drift — a skip
 * link pointing at an id that no longer exists is worse than no skip link, and
 * it fails silently for exactly the users who rely on it.
 */
export const MAIN_CONTENT_ID = 'main-content';

/**
 * The first thing a keyboard user reaches on any screen.
 *
 * Every shell in this app puts navigation before content — the organizer rail
 * has fourteen links, the public sheet has a header — so without this, reaching
 * the actual page means tabbing past all of it on every single navigation.
 *
 * Visually absent until focused, and then a real, visible control at the top
 * left rather than a 1px sliver: `sr-only` alone would leave it invisible even
 * to the sighted keyboard user it exists for, so `focus:not-sr-only` promotes
 * it to a positioned button. It carries the app's own `FOCUS_RING`, so it looks
 * like every other focused control rather than like a browser default.
 *
 * `href` rather than a scroll handler: moving focus is the whole point, and a
 * same-document link is the one thing that does it natively in every browser.
 */
export function SkipLink() {
  return (
    <a
      href={`#${MAIN_CONTENT_ID}`}
      className={`sr-only focus:not-sr-only focus:absolute focus:left-4 focus:top-4 focus:z-50 focus:rounded-lg focus:bg-accent-600 focus:px-4 focus:py-2 focus:text-sm focus:font-medium focus:text-white focus:shadow-lg ${FOCUS_RING}`}
    >
      Skip to main content
    </a>
  );
}

type Icon = ComponentType<{ className?: string }>;

/**
 * The title block every screen opens with, and the app's largest text.
 *
 * 24px: the rail says where you are, but the title is still the anchor you land
 * on, and at 16px it was smaller than the body text of most desktop software.
 */
export function PageHeader({
  title,
  description,
  actions,
}: {
  title: string;
  description?: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <header className="mb-8 flex flex-wrap items-start justify-between gap-x-6 gap-y-3">
      {/* `flex-1` so the title block yields width instead of claiming its
          description's full measure — without it a long description pushes the
          actions onto their own line, where they read as stray content. */}
      <div className="min-w-0 flex-1">
        <h1 className="text-2xl font-semibold tracking-tight text-gray-900">{title}</h1>
        {description && <p className="mt-1.5 max-w-2xl text-[15px] leading-relaxed text-gray-500">{description}</p>}
      </div>
      {actions && <div className="flex shrink-0 flex-wrap items-center gap-2">{actions}</div>}
    </header>
  );
}

/**
 * A titled group, separated by a rule and whitespace rather than boxed in.
 *
 * A border should mean "this is a distinct object"; when every group has one it
 * means nothing and the page becomes a stack of rectangles. The hairline above
 * the heading gives the separation a card was being used for, at a fraction of
 * the ink.
 */
export function Section({
  title,
  description,
  actions,
  children,
  divided = true,
  className = '',
}: {
  title?: string;
  description?: ReactNode;
  actions?: ReactNode;
  children: ReactNode;
  /** Set false for the first section on a page, where a rule under the header would double up. */
  divided?: boolean;
  className?: string;
}) {
  // A <section> is only a landmark once it has an accessible name, so wiring
  // the heading to it makes each of these a region a screen reader can jump
  // between — the structure the removed borders implied visually.
  const headingId = useId();
  return (
    <section
      className={`${divided ? 'border-t border-gray-200 pt-8' : ''} ${className}`}
      aria-labelledby={title ? headingId : undefined}
    >
      {(title || actions) && (
        <div className="mb-5 flex flex-wrap items-start justify-between gap-x-6 gap-y-2">
          <div className="min-w-0">
            {title && <h2 id={headingId} className={SECTION_HEADING}>{title}</h2>}
            {description && <p className="mt-1 max-w-2xl text-sm leading-relaxed text-gray-500">{description}</p>}
          </div>
          {actions && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
        </div>
      )}
      {children}
    </section>
  );
}

export type NoticeTone = 'success' | 'error' | 'warning' | 'info';

const NOTICE_TONE: Record<NoticeTone, string> = {
  success: 'border-accent-200 bg-accent-50 text-accent-800',
  error: 'border-red-200 bg-red-50 text-red-700',
  warning: 'border-amber-200 bg-amber-50 text-amber-900',
  info: 'border-blue-200 bg-blue-50 text-blue-900',
};

/**
 * Feedback after an action. Errors carry `role="alert"` so assistive tech
 * announces them — the grey "Saved."/"Failed to save." span this replaced was
 * both silent and identical in either direction.
 */
export function InlineNotice({
  tone,
  children,
  className = '',
}: {
  tone: NoticeTone;
  children: ReactNode;
  className?: string;
}) {
  return (
    <div
      role={tone === 'error' ? 'alert' : 'status'}
      className={`flex items-start gap-2.5 rounded-lg border px-4 py-3 text-sm ${NOTICE_TONE[tone]} ${className}`}
    >
      {children}
    </div>
  );
}

/**
 * "Are you sure?", in the app rather than in the browser.
 *
 * Three destructive actions used to ask in three different ways: the session
 * screen revealed an inline panel, while deleting a booking page and
 * disconnecting a calendar both called `window.confirm`. A native dialog cannot
 * be styled, cannot carry a link, truncates in some browsers, is suppressible,
 * and — the reason it actually matters here — flattens a consequence worth
 * several lines into one run-on string. Disconnecting a calendar silently stops
 * Google Meet links being created; that is exactly the kind of thing a
 * confirmation exists to say, and exactly what a one-line dialog buries.
 *
 * Not a framework and not a modal: it is a panel the caller renders in place,
 * with its own state, in the same shape `EmptyState` and `InlineNotice` are.
 * Nothing here traps focus or covers the page, because none of these actions
 * needs the rest of the screen taken away — and an inline panel keeps the thing
 * being acted on visible while you decide.
 *
 * The confirm button is **first in the DOM and last on screen** at `sm` and up,
 * the same arrangement the public cancel screen uses: the safe option should be
 * the easy one to reach, and a stray Enter should never land on the
 * destructive one.
 */
export function ConfirmPanel({
  title,
  children,
  confirmLabel,
  busyLabel,
  onConfirm,
  onCancel,
  busy = false,
  cancelLabel = 'Keep it',
}: {
  title: string;
  /** What actually happens. Say the consequence, not "this cannot be undone". */
  children?: ReactNode;
  confirmLabel: string;
  busyLabel?: string;
  onConfirm: () => void;
  onCancel: () => void;
  busy?: boolean;
  cancelLabel?: string;
}) {
  const headingId = useId();
  return (
    <div
      role="group"
      aria-labelledby={headingId}
      className="rounded-lg border border-red-200 bg-red-50 px-4 py-3.5"
    >
      <p id={headingId} className="text-[15px] font-semibold text-red-900">{title}</p>
      {children && <div className="mt-1.5 text-sm leading-relaxed text-red-900">{children}</div>}

      <div className="mt-4 flex flex-col-reverse gap-2 sm:flex-row-reverse sm:justify-end">
        <button type="button" onClick={onConfirm} disabled={busy} className={BUTTON_DANGER_SOLID}>
          {busy ? (busyLabel ?? confirmLabel) : confirmLabel}
        </button>
        <button type="button" onClick={onCancel} disabled={busy} className={BUTTON_SECONDARY}>
          {cancelLabel}
        </button>
      </div>
    </div>
  );
}

/**
 * "Nothing here yet", with the reason and — where there is one — the action
 * that fixes it.
 *
 * The icon sits in a tinted disc rather than floating loose: an empty state is
 * the one place in a dense UI with room for a little warmth, and a bare 16px
 * glyph over two grey lines is the characterless version of this.
 */
export function EmptyState({
  icon: IconComponent,
  title,
  description,
  action,
  compact = false,
}: {
  icon?: Icon;
  title: string;
  description?: ReactNode;
  action?: ReactNode;
  /** For an empty panel inside a grid, where a full-height block would blow out the row. */
  compact?: boolean;
}) {
  return (
    <div
      className={`rounded-lg border border-dashed border-gray-300 bg-gray-50/60 text-center ${
        compact ? 'px-6 py-8' : 'px-6 py-14'
      }`}
    >
      {IconComponent && (
        <span className="mx-auto mb-3 flex h-11 w-11 items-center justify-center rounded-full bg-white text-accent-600 ring-1 ring-gray-200">
          <IconComponent className="h-5 w-5" aria-hidden="true" />
        </span>
      )}
      <p className="text-base font-medium text-gray-900">{title}</p>
      {description && (
        <p className="mx-auto mt-1.5 max-w-md text-sm leading-relaxed text-gray-500">{description}</p>
      )}
      {action && <div className="mt-5 flex justify-center">{action}</div>}
    </div>
  );
}

/**
 * A page's data could not be loaded — the counterpart to `SkeletonLines`, and
 * the thing that used to be missing when one resolved the wrong way.
 *
 * Nearly every screen in the app fetched with no `catch` at all, so a failed
 * request left the skeleton on screen for ever: indistinguishable from a slow
 * network, with no way to retry short of reloading the tab. Worse where a list
 * was involved, because the alternative outcome was an *empty state* — "No
 * questions yet", "Nothing available in August" — which is not "we could not
 * ask", it is the opposite answer.
 *
 * This is `SessionDetailPage`'s treatment, which was the one screen that got
 * this right, lifted verbatim. It lives here rather than being copied to each
 * page for the same reason every other constant in this file does: thirteen
 * hand-written copies of a notice is how the app previously ended up with four
 * status pills and five greys.
 *
 * `onRetry` is optional because retrying is not always meaningful — a mistyped
 * public token will fail identically every time — but for an organizer screen
 * behind a transient API failure it is the whole point, so pass it wherever the
 * request can simply be made again.
 */
export function LoadError({
  message,
  onRetry,
  className = '',
}: {
  message: string;
  onRetry?: () => void;
  className?: string;
}) {
  return (
    <InlineNotice tone="error" className={className}>
      <span>
        {message}
        {onRetry && (
          <>
            {' '}
            <button
              type="button"
              onClick={onRetry}
              className={`rounded font-medium underline ${FOCUS_RING}`}
            >
              Try again
            </button>
          </>
        )}
      </span>
    </InlineNotice>
  );
}

/**
 * In-place loading. Settings pages used to return a full-screen "Loading..."
 * that removed the header and every navigation control, then snapped back.
 *
 * The accessible name is the stable hook for tests — a literal string in the
 * markup is a rendering detail, `aria-label="Loading"` is the contract.
 */
export function SkeletonLines({ lines = 4, className = '' }: { lines?: number; className?: string }) {
  return (
    <div className={`space-y-3 ${className}`} role="status" aria-label="Loading">
      {Array.from({ length: lines }).map((_, i) => (
        <div key={i} className="h-4 animate-pulse rounded bg-gray-100" style={{ width: `${92 - i * 9}%` }} />
      ))}
    </div>
  );
}

/** Skeleton shaped like a panel, so the layout does not jump when it resolves. */
export function SkeletonCard({ lines = 4 }: { lines?: number }) {
  return (
    <div className={`${CARD} p-6`}>
      <div className="mb-5 h-4 w-40 animate-pulse rounded bg-gray-200" />
      <SkeletonLines lines={lines} />
    </div>
  );
}

/**
 * A status, as a coloured dot beside plain text.
 *
 * The app had four independent filled-pill implementations (session status,
 * reminder status, calendar health, booking page enabled state), each with its
 * own colour map. A filled pill is a heavy mark: at list density it becomes the
 * loudest thing on the row, competing with the content it annotates. A dot
 * carries the same categorical information far more quietly, and — unlike a
 * colour-only pill — the label always sits beside it.
 */
export type StatusTone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';

const DOT_TONE: Record<StatusTone, string> = {
  neutral: 'bg-gray-400',
  info: 'bg-blue-500',
  success: 'bg-accent-500',
  warning: 'bg-amber-500',
  danger: 'bg-red-500',
};

export function StatusPill({
  tone,
  children,
  detail,
}: {
  tone: StatusTone;
  children: ReactNode;
  /** A qualifier shown in lighter text after the label, e.g. "Submitted · Upcoming". */
  detail?: ReactNode;
}) {
  return (
    <span className="inline-flex items-center gap-2 whitespace-nowrap text-[13px] text-gray-700">
      <span aria-hidden="true" className={`h-2 w-2 shrink-0 rounded-full ${DOT_TONE[tone]}`} />
      <span className="font-medium">{children}</span>
      {detail && (
        <>
          <span aria-hidden="true" className="text-gray-300">·</span>
          <span className="text-gray-500">{detail}</span>
        </>
      )}
    </span>
  );
}
