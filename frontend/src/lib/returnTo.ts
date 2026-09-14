/**
 * Where to send an organizer after they sign in.
 *
 * `ProtectedRoute` records the address that was refused and `LoginPage` reads
 * it back, so a bookmarked deep link survives the trip through authentication
 * instead of always landing on `/dashboard`. That was worth fixing on its own,
 * and it also completes a decision the router already made: the three legacy
 * settings redirects sit outside the layout group specifically so a signed-out
 * organizer following an old bookmark resolves the *renamed* route rather than
 * losing it - which only helps if the renamed route then survives the login.
 *
 * A return destination is an open redirect if it is taken on trust, so this is
 * the one place that decides whether a candidate is usable, and it accepts only
 * an internal path.
 */

/** The default when there is nowhere to return to - an ordinary sign-in. */
export const DEFAULT_SIGNED_IN_PATH = '/dashboard';

/** Never a return destination: landing back on either would loop straight round again. */
const AUTH_PATHS = ['/login', '/register'];


/** `scheme:` at the start - "https:", "javascript:", "data:". */
const HAS_SCHEME = /^[a-z][a-z0-9+.-]*:/i;

/**
 * Tab, newline, CR and the rest of C0/DEL. Checked by code point rather than by
 * a regex holding the raw bytes, which would put unreadable control characters
 * into this file. URL parsing strips these, which is exactly what makes them
 * useful for hiding one of the patterns checked below.
 */
function hasControlCharacter(value: string): boolean {
  for (let i = 0; i < value.length; i++) {
    const code = value.charCodeAt(i);
    if (code < 0x20 || code === 0x7f) return true;
  }
  return false;
}

/**
 * The internal path carried by `value`, or null when there is nothing usable.
 *
 * Accepts either a router `Location` (what `ProtectedRoute` stores, so the query
 * string and hash come along) or a plain string. Everything else - an absolute
 * URL, a protocol-relative `//evil.example`, the backslash variants several
 * browsers normalise into one, a path that does not start at the root - is
 * rejected rather than sanitised, because a half-repaired redirect target is
 * worse than no redirect at all.
 */
export function safeReturnTo(value: unknown): string | null {
  const path = toPath(value);
  if (path === null) return null;

  // Stripped first, so a hidden "//" cannot slip past the checks below.
  if (hasControlCharacter(path)) return null;
  // Must be root-relative...
  if (!path.startsWith('/')) return null;
  // ...and not the forms that leave the site despite starting with a slash.
  if (path.startsWith('//') || path.startsWith('/\\')) return null;
  // A scheme means this was never a path.
  if (HAS_SCHEME.test(path) || path.includes('://')) return null;

  const pathname = path.split(/[?#]/)[0];
  if (AUTH_PATHS.includes(pathname)) return null;

  return path;
}

/** A router Location, or a plain string, reduced to `pathname + search + hash`. */
function toPath(value: unknown): string | null {
  if (typeof value === 'string') return value.trim() === '' ? null : value;
  if (typeof value !== 'object' || value === null) return null;

  const location = value as { pathname?: unknown; search?: unknown; hash?: unknown };
  if (typeof location.pathname !== 'string' || location.pathname === '') return null;

  const search = typeof location.search === 'string' ? location.search : '';
  const hash = typeof location.hash === 'string' ? location.hash : '';
  return `${location.pathname}${search}${hash}`;
}
