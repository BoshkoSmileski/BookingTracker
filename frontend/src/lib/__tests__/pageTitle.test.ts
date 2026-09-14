import { describe, expect, it } from 'vitest';
import { APP_NAME, pageTitle } from '../pageTitle';

/**
 * The suffix rule, in one place. The hook itself is exercised through the
 * pages that call it (see `documentTitle.test.tsx`), because what matters is
 * that a route sets the right title - not that `useEffect` assigns a string.
 */
describe('pageTitle', () => {
  it('appends the product name to a page name', () => {
    expect(pageTitle('Working hours')).toBe('Working hours · BookingTracker');
  });

  it('is the bare product name for the landing page and for a title not yet known', () => {
    // Both cases are real: `/` has no segment, and a page whose title depends
    // on a fetch passes null until it resolves.
    expect(pageTitle(null)).toBe(APP_NAME);
    expect(pageTitle(undefined)).toBe(APP_NAME);
    expect(pageTitle('   ')).toBe(APP_NAME);
  });
});
