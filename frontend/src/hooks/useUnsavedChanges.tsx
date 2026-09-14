import { useCallback, useEffect, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { ConfirmPanel, FOCUS_RING } from '../components/ui';

/**
 * The one wording this app uses to say work is about to be thrown away.
 *
 * Exported for the same reason `COPIED_LABEL` is: several settings screens ask
 * this question and there is no reason for any of them to phrase it
 * differently.
 */
export const UNSAVED_CHANGES_TITLE = 'Unsaved changes';
export const LEAVE_LABEL = 'Leave without saving';
export const STAY_LABEL = 'Stay on this page';

/**
 * Structural equality over the plain values these forms hold - strings,
 * numbers, booleans, arrays and object literals.
 *
 * Deliberately private, and deliberately not `JSON.stringify`. Stringify is
 * key-order sensitive, so a draft assembled by one code path and a baseline
 * assembled by another (`toWeeklyHours` on load versus `copyDayToWeekdays`
 * after an edit) could compare unequal while holding identical data - a false
 * "unsaved changes" warning, which is the one failure mode this feature must
 * not have. It also silently drops `undefined`, which would make an absent
 * field and a present-but-undefined one look the same.
 */
function isSameDraft(a: unknown, b: unknown): boolean {
  if (Object.is(a, b)) return true;
  if (typeof a !== 'object' || typeof b !== 'object' || a === null || b === null) return false;

  if (Array.isArray(a) || Array.isArray(b)) {
    if (!Array.isArray(a) || !Array.isArray(b) || a.length !== b.length) return false;
    return a.every((item, index) => isSameDraft(item, b[index]));
  }

  const left = a as Record<string, unknown>;
  const right = b as Record<string, unknown>;
  const keys = Object.keys(left);
  if (keys.length !== Object.keys(right).length) return false;
  return keys.every((key) => Object.hasOwn(right, key) && isSameDraft(left[key], right[key]));
}

export interface UnsavedChanges {
  /** The editable values differ from the last persisted state this page adopted. */
  isDirty: boolean;
  /**
   * "What is on screen now IS the persisted state." Call it wherever the page
   * adopts server data - after a successful load, after a successful save, and
   * after any refresh that becomes the new authoritative state.
   *
   * Safe to call in the same callback as the `setState` calls that apply that
   * data: the baseline is captured in an effect after the render those updates
   * produce, not from the closure it was called in. A version that snapshotted
   * immediately would read the value being replaced, which is the one way this
   * could quietly mark a page permanently dirty.
   */
  markSaved: () => void;
  /** Render this somewhere visible. `null` unless a navigation is being held. */
  prompt: ReactNode;
}

/**
 * Warn before unsaved settings are abandoned.
 *
 * Nothing in this app guarded against this before: an organizer who edited a
 * form and then clicked a rail link lost the edits silently, with nothing on
 * screen having said so. `WorkingHoursPage` is the worst case - a whole week of
 * availability. This is the navigation-time counterpart of the rules that made
 * a failed *action* and a failed *load* report
 * themselves honestly; work that vanishes on the way out is the same defect in
 * a third place.
 *
 * ## Why navigation is caught at the anchor rather than with `useBlocker`
 *
 * React Router ships `useBlocker`, and it is the obvious answer - but it calls
 * `useDataRouterContext`, which throws unless it is rendered under a **data**
 * router (`createBrowserRouter`). This app mounts `<BrowserRouter>` around a
 * declarative `<Routes>` tree, and the 35 test files that go through
 * `renderWithProviders` mount `<MemoryRouter>`. Migrating the app's root
 * routing to a data router to add a polish feature is exactly the kind of
 * rewrite this project avoids, and it would rewrite the harness under a green suite for no
 * behaviour anybody asked for.
 *
 * So this listens for clicks on anchors in the **capture phase at the
 * document**, which is above the root container React attaches its own
 * listeners to - early enough to stop the event before `Link`'s handler ever
 * sees it, and covering every `Link` and `NavLink` in the app (the rail, the
 * drawer, in-page links) without any of them being touched. `preventDefault`
 * alone would not do: it stops the browser, not `Link`, which would call
 * `navigate()` regardless. Both are needed.
 *
 * What it deliberately does not catch is recorded under Remaining limitations:
 * the browser's Back/Forward buttons (a `popstate` cannot be cancelled without
 * pushing a compensating entry, which fights the app's own `replace: true`
 * history use), and programmatic `navigate()` from a button - of which the only
 * one that leaves a guarded page is Sign out, an unambiguous "I am done".
 */
export function useUnsavedChanges(draft: unknown): UnsavedChanges {
  const navigate = useNavigate();
  const location = useLocation();
  const currentPath = `${location.pathname}${location.search}${location.hash}`;

  /**
   * `null` until the page first adopts persisted state, and that default is
   * load-bearing: a page that never calls `markSaved` warns about nothing
   * rather than warning about everything. A baseline taken at mount would mark
   * every one of these screens dirty the moment its own data arrived, since
   * they all render placeholder state first (`emptyWeek()` is seven closed
   * days). Wrapped in an object so a legitimately `undefined` draft stays
   * distinguishable from "no baseline yet".
   */
  const [baseline, setBaseline] = useState<{ value: unknown } | null>(null);
  const [pendingPath, setPendingPath] = useState<string | null>(null);
  const [adoptToken, setAdoptToken] = useState(0);

  const draftRef = useRef(draft);
  // Declared before the adopt effect below and given no dependency array, so it
  // has already run by the time that one reads the ref. Effects run in the
  // order their hooks were called.
  useEffect(() => {
    draftRef.current = draft;
  });

  useEffect(() => {
    if (adoptToken === 0) return;
    setBaseline({ value: draftRef.current });
  }, [adoptToken]);

  const markSaved = useCallback(() => setAdoptToken((token) => token + 1), []);

  const isDirty = baseline !== null && !isSameDraft(draft, baseline.value);

  // A save that succeeds while the prompt is open leaves nothing to warn about.
  useEffect(() => {
    if (!isDirty) setPendingPath(null);
  }, [isDirty]);

  /**
   * Closing the tab, reloading, or following a link out of the app. The browser
   * draws its own confirmation and ignores any message supplied here, so there
   * is nothing to word - only whether to ask at all.
   */
  useEffect(() => {
    if (!isDirty) return;
    const warn = (event: BeforeUnloadEvent) => {
      event.preventDefault();
      // Still read by Chrome and Safari; `preventDefault` is the modern spec
      // and is not yet enough on its own everywhere.
      event.returnValue = '';
    };
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [isDirty]);

  useEffect(() => {
    if (!isDirty) return;

    const holdNavigation = (event: MouseEvent) => {
      if (event.defaultPrevented || event.button !== 0) return;
      // A modified click opens a new tab or window, so this page is not going
      // anywhere and there is nothing to protect.
      if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;

      const anchor = (event.target as Element | null)?.closest?.('a[href]') as HTMLAnchorElement | null;
      if (!anchor) return;

      // `SkipLink` and any other same-document jump. Read off the raw attribute
      // rather than the resolved URL because under `MemoryRouter` the router's
      // location and `window.location` are two different things.
      if ((anchor.getAttribute('href') ?? '').startsWith('#')) return;
      if (anchor.hasAttribute('download')) return;
      if (anchor.target && anchor.target !== '_self') return;

      let url: URL;
      try {
        url = new URL(anchor.href, window.location.href);
      } catch {
        return;
      }
      // Leaves the app entirely (including `mailto:`, whose origin is "null"),
      // so the browser unloads the page and `beforeunload` above is what asks.
      if (url.origin !== window.location.origin) return;

      const to = `${url.pathname}${url.search}${url.hash}`;
      if (to === currentPath) return;

      /*
        `preventDefault` alone is what holds the navigation, and it is enough:
        `Link` calls its own `onClick` prop first and then navigates only
        `if (!event.defaultPrevented)`, so a default already prevented by this
        capture-phase listener stops it. Verified against the installed
        react-router (`Link.handleClick`), and both `Link` and `NavLink` go
        through it.

        This deliberately does **not** `stopPropagation`, which it used to. That
        swallowed the click entirely, including the `onClick` the shell puts on
        every rail link to close its mobile drawer - so below `lg` a held
        navigation left the drawer open on top of the very panel asking the
        question, and the tap looked like a link that did nothing. Letting the
        click through costs nothing here: the only `onClick` on any link in this
        app is that drawer-closing callback, and a handler that runs before the
        `defaultPrevented` check cannot navigate past it.
      */
      event.preventDefault();
      setPendingPath(to);
    };

    document.addEventListener('click', holdNavigation, true);
    return () => document.removeEventListener('click', holdNavigation, true);
  }, [isDirty, currentPath]);

  const promptRef = useRef<HTMLDivElement | null>(null);
  useEffect(() => {
    if (pendingPath === null) return;
    const node = promptRef.current;
    if (!node) return;
    // These forms are long - `WorkingHoursPage` is seven rows plus a notice -
    // and the click that opened this came from the rail, so the panel can
    // easily be off-screen. Without moving to it, a held navigation looks
    // exactly like a link that did nothing.
    node.focus();
    node.scrollIntoView?.({ block: 'center' });
  }, [pendingPath]);

  const stay = useCallback(() => setPendingPath(null), []);

  const leave = useCallback(() => {
    const to = pendingPath;
    setPendingPath(null);
    // `navigate` dispatches no click, so the listener above cannot catch this
    // and hold it a second time. Nothing is saved on the way out, by design.
    if (to !== null) void navigate(to);
  }, [pendingPath, navigate]);

  const prompt =
    pendingPath === null ? null : (
      <div ref={promptRef} tabIndex={-1} className={`${FOCUS_RING} rounded-lg`}>
        <ConfirmPanel
          title={UNSAVED_CHANGES_TITLE}
          confirmLabel={LEAVE_LABEL}
          cancelLabel={STAY_LABEL}
          onConfirm={leave}
          onCancel={stay}
        >
          Your changes on this page have not been saved yet. Leaving now discards them; staying
          keeps everything you have typed so you can save first.
        </ConfirmPanel>
      </div>
    );

  return { isDirty, markSaved, prompt };
}
