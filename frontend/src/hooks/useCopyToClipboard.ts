import { useCallback, useEffect, useRef, useState } from 'react';

/** How long the "Copied!" echo stays up. */
const RESET_MS = 2000;

/**
 * The one word this app uses to confirm a copy.
 *
 * Exported so the four call sites cannot drift apart again — three said
 * "Copied!" and one said "Copied", which is the smallest possible version of
 * the same problem the app had with four status pills and five greys.
 */
export const COPIED_LABEL = 'Copied!';

/**
 * Copy a string, and remember briefly that it worked.
 *
 * Four screens had their own copy of this — `PublicLinkBar`,
 * `NewBookingPagePanel`, `DashboardHomePage` and `MeetingPanel` — and only
 * `MeetingPanel` got it right. The other three called `setTimeout` inside the
 * click handler with nothing clearing it, so navigating away within two seconds
 * left a timer to fire against an unmounted component; and all three used
 * `void navigator.clipboard.writeText(...)`, which reports success
 * unconditionally. Clipboard access is refused outside a secure context and in
 * some embedded browsers, so those three would show "Copied!" for a copy that
 * did not happen, and reject an unhandled promise on the way.
 *
 * The timer is the **one** legitimate auto-clearing timer in this app —
 * This is the one sanctioned case: a momentary echo of a trivial,
 * consequence-free action. It is not a toast, and this hook must not grow into
 * one; a failure here is deliberately silent (see below) rather than surfaced,
 * because the app's feedback model is inline and beside-the-thing.
 *
 * `copied` is the **text most recently copied**, not a boolean, which is what
 * lets `DashboardHomePage` share one hook across a grid of booking-page cards:
 * each card asks whether the value on screen is the one that was copied. Slugs
 * are unique, so the URLs are distinct and identity by value is exact. The
 * three single-value callers compare against their one URL and read the same
 * way.
 */
export function useCopyToClipboard() {
  const [copied, setCopied] = useState<string | null>(null);
  const timerRef = useRef<number | null>(null);

  // Cleared on unmount rather than only being rescheduled, so a copy followed
  // by navigation inside the two-second window leaves nothing pending.
  useEffect(
    () => () => {
      if (timerRef.current !== null) window.clearTimeout(timerRef.current);
    },
    [],
  );

  const copy = useCallback(async (text: string) => {
    try {
      await navigator.clipboard.writeText(text);
    } catch {
      // Refused: outside a secure context, or an embedded browser without
      // clipboard permission. Every caller renders the value it is copying in
      // full right beside the button, so the fallback is selecting it by hand —
      // which is a far better outcome than an error notice for something the
      // user can still do, and is the reasoning `MeetingPanel` already carried.
      // Nothing is claimed: `copied` is left alone, so the label stays
      // "Copy link".
      return;
    }

    setCopied(text);
    if (timerRef.current !== null) window.clearTimeout(timerRef.current);
    timerRef.current = window.setTimeout(() => {
      setCopied(null);
      timerRef.current = null;
    }, RESET_MS);
  }, []);

  return { copied, copy };
}
