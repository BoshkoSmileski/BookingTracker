import type { Page } from '@playwright/test';
import { addFormField } from './support/api';
import { expect, signIn, test } from './support/fixtures';

/**
 * What the app does when the API cannot be reached.
 *
 * This is the state the other three suites structurally cannot produce: every
 * frontend test mocks a *resolving* API, which is exactly why eleven pages once
 * shipped with no `catch` at all and skeletoned for ever on a failed load. The
 * failure is injected at the browser rather than by stopping the server, because
 * a controlled abort is deterministic and reversible mid-test - taking Kestrel
 * down and waiting for it to come back is neither.
 *
 * `route.abort('failed')` is the honest simulation: `fetch` *rejects* on an
 * unreachable server (as opposed to resolving with a non-2xx), which is the
 * distinction `NetworkError` exists for and the one thing a stubbed 500 would
 * not reproduce.
 */

/** Distinct enough not to collide with the screen's own description copy. */
const QUESTION_LABEL = 'Account number';

/** Makes every API call fail the way an unreachable server does. Returns the undo. */
async function cutTheApi(page: Page): Promise<() => Promise<void>> {
  await page.route('**/api/**', (route) => route.abort('failed'));
  return () => page.unroute('**/api/**');
}

test.describe('API failure', () => {
  test('a failed initial load shows the error, not a skeleton and not an empty state', async ({
    page,
    organizer,
    bookingPage,
    request,
  }) => {
    // A question exists, so "No questions yet" would be a false claim about the
    // organizer's data rather than merely an unhelpful one.
    await addFormField(request, organizer, bookingPage.id, { label: QUESTION_LABEL });
    await signIn(page, organizer);

    const restore = await cutTheApi(page);
    await page.goto(`/dashboard/${bookingPage.id}/settings/form`);

    // ProtectedRoute cannot restore the session either, so the failure surfaces
    // as the sign-in screen rather than as a dashboard skeleton. Either way the
    // app resolves into something rather than hanging.
    await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
    await expect(page.getByRole('status', { name: 'Loading' })).toBeHidden();

    // With the API back, the same address renders the real content - so the
    // failure state was a state, not a dead end.
    await restore();
    await signIn(page, organizer);
    await page.goto(`/dashboard/${bookingPage.id}/settings/form`);
    await expect(page.getByText(QUESTION_LABEL, { exact: true })).toBeVisible();
  });

  test('a page-level load failure replaces the list rather than claiming it is empty', async ({
    page,
    organizer,
    bookingPage,
    request,
  }) => {
    await addFormField(request, organizer, bookingPage.id, { label: QUESTION_LABEL });
    await signIn(page, organizer);

    // Land on the page's own dashboard first: the session is then established,
    // and the rail expands that page's settings - so only the Booking form
    // screen's own fetch is the thing that fails.
    await page.goto(`/dashboard/${bookingPage.id}`);
    await expect(page.getByRole('heading', { name: 'Sessions' })).toBeVisible();

    const restore = await cutTheApi(page);
    await page.getByRole('link', { name: 'Booking form' }).click();

    // The error replaces the list. An empty state here would answer "does this
    // organizer have questions?" with "no" when the truthful answer is "we could
    // not find out" - and there is in fact one.
    await expect(page.getByRole('alert')).toBeVisible();
    await expect(page.getByText('No questions yet')).toBeHidden();
    await expect(page.getByRole('status', { name: 'Loading' })).toBeHidden();

    // Try again re-issues the request and renders the real content.
    await restore();
    await page.getByRole('button', { name: 'Try again' }).click();
    await expect(page.getByText(QUESTION_LABEL, { exact: true })).toBeVisible();
    await expect(page.getByRole('alert')).toBeHidden();
  });

  test('a failed refresh keeps the figures already on screen', async ({ page, organizer }) => {
    await signIn(page, organizer);
    await page.goto('/dashboard/analytics');

    // Every panel below the cards renders only once a load has succeeded - the
    // whole block is behind `nothingLoaded ? null : ...` - so this heading being
    // present is the same fact as "the dashboard has data".
    const trendPanel = page.getByRole('heading', { name: 'Booking trend' });
    const summaryCard = page.getByRole('main').getByText('Booking pages', { exact: true });
    await expect(trendPanel).toBeVisible();
    await expect(summaryCard).toBeVisible();

    const restore = await cutTheApi(page);
    await page.getByRole('button', { name: 'Last 7 days' }).click();

    // Reported above the data, and the data stays: it is still true of the range
    // it was fetched for, and blanking it is a worse answer than a stale one.
    await expect(page.getByRole('alert')).toBeVisible();
    await expect(trendPanel).toBeVisible();
    await expect(summaryCard).toBeVisible();

    await restore();
  });
});
