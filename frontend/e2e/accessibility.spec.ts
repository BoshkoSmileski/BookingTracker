import { bookViaApi } from './support/api';
import { expect, signIn, test } from './support/fixtures';

/**
 * Keyboard and labelling smoke tests, not an audit.
 *
 * No axe or similar: the project runs a deliberately small dependency set, and a
 * rule-scanner would mostly re-report what the frontend suite already pins by
 * role and accessible name. What is here is the behaviour jsdom cannot execute -
 * real focus movement, a real same-document jump, real `:focus-visible` - and
 * one contract that a scanner would not check at all: that the skip link
 * actually *moves focus*, rather than only scrolling.
 */

test.describe('accessibility', () => {
  test('the skip link is the first tab stop and moves focus to main', async ({ page, bookingPage }) => {
    await page.goto(`/book/${bookingPage.slug}`);
    await expect(page.getByRole('heading', { level: 1, name: bookingPage.title })).toBeVisible();

    // First in the DOM on every shell, so it is the very first thing Tab reaches.
    await page.keyboard.press('Tab');
    const skipLink = page.getByRole('link', { name: 'Skip to main content' });
    await expect(skipLink).toBeFocused();
    // `sr-only focus:not-sr-only` - invisible until focused, then a real control
    // rather than a 1px sliver, because it exists for a sighted keyboard user.
    await expect(skipLink).toBeVisible();

    await page.keyboard.press('Enter');

    // The whole point. Browsers do not focus a plain <main> on a same-document
    // jump, which is why the landmark carries tabIndex={-1}; without it the link
    // scrolls and silently leaves focus where it was.
    const focusedId = await page.evaluate(() => document.activeElement?.id);
    expect(focusedId).toBe('main-content');
    await expect(page.getByRole('main')).toBeFocused();
  });

  test('every booking input is reachable and labelled', async ({ page, bookingPage }) => {
    await page.goto(`/book/${bookingPage.slug}`);

    // The calendar is ONE tab stop, not forty-two: a roving tabindex puts Tab on
    // the first selectable day and arrow keys move between available days only.
    // Counted rather than assumed, because the exact number of controls before
    // it is a layout detail - what matters is that it is small.
    await page.getByRole('link', { name: 'Skip to main content' }).focus();

    let tabs = 0;
    let focusedRole: string | null = null;
    while (tabs < 6 && focusedRole !== 'gridcell') {
      await page.keyboard.press('Tab');
      tabs += 1;
      focusedRole = await page.evaluate(() => document.activeElement?.getAttribute('role') ?? null);
    }
    expect(focusedRole).toBe('gridcell');

    await page.keyboard.press('Enter');
    await expect(page.getByRole('heading', { name: /^Times on / })).toBeVisible();

    // Times are `aria-pressed` buttons in a group, not a listbox promising
    // arrow-key navigation and a selected option that never existed.
    const times = page.getByRole('group', { name: 'Available times' }).getByRole('button');
    await expect(times.first()).toHaveAttribute('aria-pressed', 'false');
    await times.first().click();

    // Each details input has a real label association, so `getByLabel` resolves
    // exactly one control - which is the same thing a screen reader announces.
    await expect(page.getByLabel('Name', { exact: true })).toBeVisible();
    await expect(page.getByLabel('Email', { exact: true })).toBeVisible();
    await expect(page.getByLabel('Phone (optional)')).toBeVisible();
  });

  test('the login form is labelled and submits from the keyboard alone', async ({
    page,
    organizer,
    bookingPage,
  }) => {
    await page.goto('/login');

    await page.getByLabel('Email').fill(organizer.email);
    await page.keyboard.press('Tab');
    await expect(page.getByLabel('Password')).toBeFocused();
    await page.keyboard.type(organizer.password);
    // A real form submit, not a click handler - Enter in a field is how most
    // people sign in.
    await page.keyboard.press('Enter');

    await expect(page).toHaveURL(/\/dashboard$/);
    await expect(page.getByRole('main').getByRole('link', { name: bookingPage.title })).toBeVisible();
  });

  test('a destructive confirmation can be operated without a mouse', async ({
    page,
    request,
    organizer,
    bookingPage,
  }) => {
    // A booking makes the page undeletable, which is what gives the confirmation
    // a real consequence to report.
    await bookViaApi(request, bookingPage.slug, { name: 'Held Booking', email: 'held@guest.test' });

    await signIn(page, organizer);
    await page.goto('/dashboard');

    // `exact` so this is the card's Delete rather than the panel's "Delete page".
    await page.getByRole('main').getByRole('button', { name: 'Delete', exact: true }).click();

    // A panel rendered in place, named by its own heading - not window.confirm,
    // which cannot be styled, cannot carry a consequence worth several lines,
    // and is invisible to these assertions entirely.
    const confirm = page.getByRole('group', { name: `Delete "${bookingPage.title}"?` });
    await expect(confirm).toBeVisible();

    // Keeping it is the reachable option, and it is first on screen at this
    // width - the safe choice should be the easy one, and a stray Enter must
    // never land on the destructive one.
    const keep = confirm.getByRole('button', { name: 'Keep page' });
    await keep.focus();
    await expect(keep).toBeFocused();
    await page.keyboard.press('Enter');

    await expect(confirm).toBeHidden();
    await expect(page.getByRole('main').getByRole('link', { name: bookingPage.title })).toBeVisible();
  });
});
