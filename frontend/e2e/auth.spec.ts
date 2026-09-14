import type { Page } from '@playwright/test';
import { expect, signIn, test } from './support/fixtures';

/**
 * A booking page's card in the workspace grid.
 *
 * Scoped to `main` on purpose: the rail lists every page by the same name, so an
 * unscoped lookup matches twice and would pass on the navigation alone - which
 * is exactly the thing that is still there when the content has failed to load.
 * The card's title is a link, not a heading.
 */
function workspaceCard(page: Page, title: string) {
  return page.getByRole('main').getByRole('link', { name: title, exact: true });
}

/**
 * Signing in, staying signed in, and being sent where you were going.
 *
 * The reload test is the one that could not be written anywhere else. Refresh
 * tokens are one-time-use, React StrictMode double-invokes the mount effect that
 * spends one, and `AuthContext` coalesces concurrent callers onto a single
 * request so the second invocation joins the first instead of
 * redeeming an already-revoked token. The frontend suite pins the coalescing
 * against a stubbed `api.auth.refresh`; only a real browser against a real
 * rotating-token endpoint can show that the organizer is still signed in
 * afterwards.
 */

test.describe('authentication', () => {
  test('valid credentials reach the dashboard', async ({ page, organizer, bookingPage }) => {
    await page.goto('/login');
    await page.getByLabel('Email').fill(organizer.email);
    await page.getByLabel('Password').fill(organizer.password);
    await page.getByRole('button', { name: 'Sign in' }).click();

    await expect(page).toHaveURL(/\/dashboard$/);
    await expect(page.getByRole('heading', { name: 'Booking pages' })).toBeVisible();
    await expect(workspaceCard(page, bookingPage.title)).toBeVisible();
  });

  test('invalid credentials show the error and stay on the form', async ({ page, organizer }) => {
    await page.goto('/login');
    await page.getByLabel('Email').fill(organizer.email);
    await page.getByLabel('Password').fill('not-the-right-password');
    await page.getByRole('button', { name: 'Sign in' }).click();

    // role="alert" - the notice is announced, not a silent grey span.
    await expect(page.getByRole('alert')).toBeVisible();
    await expect(page).toHaveURL(/\/login$/);
    await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
  });

  test('reloading keeps the session, spending only one refresh token', async ({
    page,
    organizer,
    bookingPage,
  }) => {
    await signIn(page, organizer);
    await page.goto('/dashboard');
    await expect(workspaceCard(page, bookingPage.title)).toBeVisible();

    // Three reloads: each one is a StrictMode-doubled mount effect racing to
    // redeem a token that can only be redeemed once. Before the shared-refresh
    // fix this presented as being "randomly logged out on reload".
    for (let i = 0; i < 3; i += 1) {
      await page.reload();
      await expect(workspaceCard(page, bookingPage.title)).toBeVisible();
      await expect(page).toHaveURL(/\/dashboard$/);
    }
  });

  test('an anonymous visitor is sent to login and then back, query string intact', async ({
    page,
    organizer,
    bookingPage,
  }) => {
    // A filtered session list - the query string is the filter, so losing it
    // would land the organizer on a different list than the one they asked for.
    const wanted = `/dashboard/${bookingPage.id}?status=Submitted`;
    await page.goto(wanted);

    await expect(page).toHaveURL(/\/login$/);

    await page.getByLabel('Email').fill(organizer.email);
    await page.getByLabel('Password').fill(organizer.password);
    await page.getByRole('button', { name: 'Sign in' }).click();

    await expect(page).toHaveURL(new RegExp(`${bookingPage.id}\\?status=Submitted$`));
    await expect(page.getByRole('button', { name: 'Submitted' })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
  });

  test('signing out ends the session for good', async ({ page, organizer, bookingPage }) => {
    await signIn(page, organizer);
    await page.goto('/dashboard');
    await expect(workspaceCard(page, bookingPage.title)).toBeVisible();

    await page.getByRole('button', { name: 'Sign out' }).click();

    // The rail is gone with the session, so the organizer is on a public surface.
    await expect(page.getByRole('button', { name: 'Sign out' })).toBeHidden();

    // And the dashboard is not reachable by going straight back to it. The
    // stored refresh token was revoked server-side by logout, so even the
    // restore-on-load path cannot resurrect it.
    await page.goto('/dashboard');
    await expect(page).toHaveURL(/\/login$/);
  });
});
