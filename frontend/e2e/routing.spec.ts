import { expect, signIn, test } from './support/fixtures';

/**
 * Every address resolves to something, and to the right kind of something.
 *
 * There was no catch-all at all until recently, so an unmatched path rendered a
 * blank white page - indistinguishable from a crash, with the URL bar as the
 * only way out. It also hid the three legacy settings redirects, which had never
 * worked: `<Navigate to="../x">` in a *flat* route resolves `..` against the
 * route hierarchy rather than the URL, so all three landed on paths that do not
 * exist. Both fixes are asserted here, in the browser that actually does the
 * resolving.
 */

test.describe('routing', () => {
  test('an unknown public address gets the guest 404, not a login prompt', async ({ page }) => {
    await page.goto('/definitely-not-a-route');

    await expect(page.getByRole('heading', { name: 'Page not found' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Go to the home page' })).toBeVisible();

    // A wrong address is not a reason to demand credentials, and a guest must
    // not be shown organizer chrome.
    await expect(page).not.toHaveURL(/\/login/);
    await expect(page.getByRole('button', { name: 'Sign out' })).toBeHidden();
    await expect(page.getByRole('navigation', { name: 'Main' })).toBeHidden();
  });

  test('an unknown dashboard address keeps the rail', async ({ page, organizer, bookingPage }) => {
    await signIn(page, organizer);

    // A path under a REAL booking page that names no screen. `/dashboard/<junk>`
    // would not reach the catch-all at all: `:pageId` is a dynamic segment, so
    // it matches, and the Sessions screen reports the API's 404 in its own
    // LoadError - which is the right answer for a page id that does not exist,
    // and a different question from an address that names no screen.
    await page.goto(`/dashboard/${bookingPage.id}/settings/not-a-real-screen`);

    await expect(page.getByRole('heading', { name: 'Page not found' })).toBeVisible();
    // `EmptyState` renders its title as a paragraph, not a heading - the page
    // title above it is the h1.
    await expect(page.getByText("This page doesn’t exist")).toBeVisible();

    // Still inside the shell: the rail, its links and the way to every other
    // screen are exactly where they were.
    const rail = page.getByRole('navigation', { name: 'Main' });
    await expect(rail).toBeVisible();
    await expect(rail.getByRole('link', { name: bookingPage.title })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Back to booking pages' })).toBeVisible();
  });

  test('the legacy settings bookmarks still land on the renamed screens', async ({
    page,
    organizer,
    bookingPage,
  }) => {
    await signIn(page, organizer);

    // `settings/questions` was renamed when the feature was renamed, and the two
    // date screens were consolidated into one. Each is a real bookmark an
    // organizer may hold. `relative="path"` is what makes these resolve at all.
    await page.goto(`/dashboard/${bookingPage.id}/settings/questions`);
    await expect(page).toHaveURL(new RegExp(`${bookingPage.id}/settings/instructions$`));
    await expect(page.getByRole('heading', { name: 'Booking instructions' })).toBeVisible();

    await page.goto(`/dashboard/${bookingPage.id}/settings/blocked-dates`);
    await expect(page).toHaveURL(new RegExp(`${bookingPage.id}/settings/date-exceptions$`));

    await page.goto(`/dashboard/${bookingPage.id}/settings/date-overrides`);
    await expect(page).toHaveURL(new RegExp(`${bookingPage.id}/settings/date-exceptions$`));
    await expect(page.getByRole('heading', { name: 'Date exceptions' })).toBeVisible();
  });
});
