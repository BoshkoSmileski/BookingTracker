import { test as base, expect, type Page } from '@playwright/test';
import {
  createBookingPage,
  registerOrganizer,
  type TestBookingPage,
  type TestOrganizer,
} from './api';

/**
 * A fresh organizer and a fresh booking page per test.
 *
 * Isolation by construction rather than by cleanup: every fixture is created
 * under a unique email and slug, and an organizer only ever sees their own data,
 * so nothing a test does is visible to any other test - or to a previous run's
 * leftovers in the E2E database. That is what lets the suite skip a teardown
 * step whose failure would silently poison later runs.
 */
export const test = base.extend<{
  organizer: TestOrganizer;
  bookingPage: TestBookingPage;
}>({
  organizer: async ({ request }, use) => {
    await use(await registerOrganizer(request));
  },

  bookingPage: async ({ request, organizer }, use) => {
    await use(await createBookingPage(request, organizer));
  },
});

export { expect };

/** Where `AuthContext` persists the refresh token. Mirrored, not imported - the app keeps it private. */
const REFRESH_TOKEN_STORAGE_KEY = 'bookingtracker.refreshToken';

/**
 * Signs an organizer in the way a returning one is signed in: a refresh token in
 * `localStorage`, redeemed for an access token by `AuthContext`'s mount effect.
 *
 * Not a shortcut around the real auth path - it *is* the real auth path, and the
 * more interesting half. `auth.spec.ts` covers the login form itself; every
 * other dashboard test arrives through this, which means every one of them also
 * exercises the reload-restore refresh under React StrictMode's double-invoked
 * mount effect. That double invocation is precisely what the shared
 * refresh (`refreshInFlightRef`) exists to survive, and a refresh token is
 * one-time-use, so a regression there would sign these tests out rather than
 * merely slowing them down.
 *
 * Planted **once**, deliberately, rather than through `addInitScript`: that runs
 * before every navigation, and refresh tokens rotate - so re-planting the
 * original on each load would overwrite the replacement `AuthContext` had just
 * stored and present a revoked token on the next reload. The symptom is being
 * signed out on reload, which is the exact bug this suite is here to catch, so a
 * fixture that manufactures it would be worse than no fixture at all.
 */
export async function signIn(page: Page, organizer: TestOrganizer): Promise<void> {
  // Any same-origin page will do; /login is the cheapest and needs no session.
  await page.goto('/login');
  await page.evaluate(
    ([key, token]) => window.localStorage.setItem(key, token),
    [REFRESH_TOKEN_STORAGE_KEY, organizer.refreshToken] as const,
  );
}
