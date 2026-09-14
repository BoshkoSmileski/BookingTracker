import { describe, expect, it } from 'vitest';
import { DEFAULT_SIGNED_IN_PATH, safeReturnTo } from '../returnTo';

/**
 * The destination an organizer is sent back to after signing in.
 *
 * Two things are being pinned, and the second matters more than the first: that
 * a bookmarked deep link survives the trip through /login, and that nothing
 * else does. A return destination taken on trust is an open redirect, so every
 * form of "starts with a slash but leaves the site" is rejected here rather
 * than repaired.
 */
describe('safeReturnTo — destinations it accepts', () => {
  it('keeps a plain internal path', () => {
    expect(safeReturnTo('/dashboard/123/settings/notifications'))
      .toBe('/dashboard/123/settings/notifications');
  });

  it('keeps the query string, which is where a list’s filter lives', () => {
    // sessionFilter puts the session status in the URL, so losing the search
    // string would return the organizer to a different list than the one they
    // linked to.
    expect(safeReturnTo('/dashboard/123?status=Submitted')).toBe('/dashboard/123?status=Submitted');
  });

  it('accepts a router Location, assembling path, search and hash', () => {
    expect(safeReturnTo({ pathname: '/dashboard/123', search: '?status=Cancelled', hash: '#top' }))
      .toBe('/dashboard/123?status=Cancelled#top');
  });

  it('tolerates a Location with no search or hash', () => {
    expect(safeReturnTo({ pathname: '/dashboard' })).toBe('/dashboard');
  });
});

describe('safeReturnTo — destinations it refuses', () => {
  it('refuses an absolute URL on another origin', () => {
    expect(safeReturnTo('https://evil.example/steal')).toBeNull();
    expect(safeReturnTo({ pathname: 'https://evil.example/steal' })).toBeNull();
  });

  it('refuses a protocol-relative URL, which looks like a path but is not', () => {
    expect(safeReturnTo('//evil.example/steal')).toBeNull();
  });

  it('refuses the backslash variants browsers normalise into a protocol-relative URL', () => {
    expect(safeReturnTo('/\\evil.example')).toBeNull();
    expect(safeReturnTo('/\\\\evil.example')).toBeNull();
  });

  it('refuses a javascript: or data: destination', () => {
    expect(safeReturnTo('javascript:alert(1)')).toBeNull();
    expect(safeReturnTo('data:text/html,<script>alert(1)</script>')).toBeNull();
  });

  it('refuses a control character used to hide one of the above', () => {
    // URL parsing strips tab/newline/CR, so "/\t/evil.example" becomes
    // protocol-relative after the check would otherwise have passed it.
    expect(safeReturnTo('/\t/evil.example')).toBeNull();
    expect(safeReturnTo('/\n/evil.example')).toBeNull();
    expect(safeReturnTo('/dash\rboard')).toBeNull();
  });

  it('refuses a relative path, which resolves against wherever the user happens to be', () => {
    expect(safeReturnTo('dashboard')).toBeNull();
    expect(safeReturnTo('../admin')).toBeNull();
  });

  it('refuses the auth screens themselves, which would loop', () => {
    expect(safeReturnTo('/login')).toBeNull();
    expect(safeReturnTo('/register')).toBeNull();
    expect(safeReturnTo({ pathname: '/login', search: '?next=/dashboard' })).toBeNull();
  });

  it('refuses anything that is not a path at all', () => {
    expect(safeReturnTo(undefined)).toBeNull();
    expect(safeReturnTo(null)).toBeNull();
    expect(safeReturnTo('')).toBeNull();
    expect(safeReturnTo('   ')).toBeNull();
    expect(safeReturnTo(42)).toBeNull();
    expect(safeReturnTo({})).toBeNull();
    expect(safeReturnTo({ pathname: 42 })).toBeNull();
  });
});

describe('DEFAULT_SIGNED_IN_PATH', () => {
  it('is the workspace, which is where an ordinary sign-in has always gone', () => {
    expect(DEFAULT_SIGNED_IN_PATH).toBe('/dashboard');
  });
});
