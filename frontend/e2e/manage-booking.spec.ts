import { bookViaApi } from './support/api';
import { expect, test } from './support/fixtures';
import { pickFirstAvailableDate, timeSlots } from './support/wizard';

/**
 * What a guest does after booking, holding nothing but a `PublicToken`.
 *
 * The booking itself is made over the API here - the wizard's own path through
 * it is covered in `public-booking.spec.ts`, and repeating it would only make
 * these tests slower. What is browser-only about these screens is that a
 * reschedule really moves the appointment the manage page then shows, and that a
 * cancelled booking stops presenting itself as a live one.
 *
 * The cancellation rules themselves (who may cancel, when, what it emails) are
 * backend tests; this is the screen.
 */

const guest = { name: 'Katherine Johnson', email: 'katherine@guest.test' };

test.describe('managing a booking', () => {
  test('the manage screen shows the booking and the way to reach the organizer', async ({
    page,
    request,
    bookingPage,
    organizer,
  }) => {
    const booking = await bookViaApi(request, bookingPage.slug, guest);

    await page.goto(`/manage/${booking.publicToken}`);

    await expect(page.getByRole('heading', { level: 1, name: bookingPage.title })).toBeVisible();
    await expect(page.getByText(booking.bookingReference)).toBeVisible();

    // Reachability, which is why `organizerEmail` is on PublicBookingDto at all:
    // the screen used to end on "contact the organizer directly" with nothing on
    // it saying how.
    await expect(page.getByRole('link', { name: organizer.email })).toHaveAttribute(
      'href',
      `mailto:${organizer.email}`,
    );

    // Built from startUtc/endUtc rather than the organizer's wall clock - the
    // reason those two fields exist.
    await expect(page.getByRole('button', { name: 'Add to calendar' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Reschedule' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Cancel booking' })).toBeVisible();
  });

  test('rescheduling moves the appointment and the manage screen agrees', async ({
    page,
    request,
    bookingPage,
  }) => {
    const booking = await bookViaApi(request, bookingPage.slug, guest);

    await page.goto(`/manage/${booking.publicToken}/reschedule`);
    await expect(page.getByRole('heading', { name: 'Pick a new time' })).toBeVisible();

    // Unlike the wizard, choosing a time here does not commit - a reschedule
    // moves something that already exists, so it is confirmed explicitly.
    const moveButton = page.getByRole('button', { name: 'Move booking' });
    await expect(moveButton).toBeDisabled();

    await pickFirstAvailableDate(page);
    // Not the first slot: the booking already holds it, so the offered list here
    // starts one later. Any slot works; taking the last makes the move visible
    // however many the day has.
    const newSlot = timeSlots(page).last();
    const newTime = (await newSlot.innerText()).split('\n')[0].trim();
    await newSlot.click();

    await expect(moveButton).toBeEnabled();
    await moveButton.click();

    await expect(page.getByRole('heading', { name: 'Booking moved' })).toBeVisible();
    // The header swaps to the NEW slot - leaving the old time up would be the
    // one screen in the flow still describing something untrue.
    await expect(page.locator('main header')).toContainText(newTime);

    // And it stuck: a fresh load of the manage screen reads the new time back
    // out of the database rather than out of this page's state.
    await page.getByRole('link', { name: 'View booking' }).click();
    await expect(page.locator('main header')).toContainText(newTime);
  });

  test('cancelling reaches a terminal state drawn in the neutral tone', async ({
    page,
    request,
    bookingPage,
  }) => {
    const booking = await bookViaApi(request, bookingPage.slug, guest);

    await page.goto(`/manage/${booking.publicToken}/cancel`);
    await expect(page.getByRole('heading', { name: 'Cancel this booking?' })).toBeVisible();

    // While the booking is live, the selection line wears the accent panel.
    const selectionLine = page.locator('main header div').filter({ hasText: '·' }).last();
    await expect(selectionLine).toHaveClass(/bg-accent-50/);

    await page.getByLabel(/^Reason/).fill('Something came up.');
    await page.getByRole('button', { name: 'Cancel booking' }).click();

    await expect(page.getByRole('heading', { name: 'Booking cancelled' })).toBeVisible();

    // Neutral now. This is the one place a class is asserted rather than a role:
    // the whole claim is that a cancelled appointment stops *looking* like a
    // confirmed one, and tone is not exposed to the accessibility tree.
    await expect(selectionLine).toHaveClass(/bg-gray-100/);
    await expect(selectionLine).not.toHaveClass(/bg-accent-50/);

    // And it is genuinely terminal: the manage screen no longer offers the
    // actions, and says so.
    await page.goto(`/manage/${booking.publicToken}`);
    await expect(page.getByText('This booking can no longer be changed.')).toBeVisible();
    await expect(page.getByRole('link', { name: 'Cancel booking' })).toBeHidden();
  });

  test('an invalid public token is a message, not a blank page or a spinner', async ({ page }) => {
    await page.goto('/manage/not-a-real-public-token');

    await expect(page.getByRole('heading', { name: "We couldn't find this booking" })).toBeVisible();
    // The skeleton resolved into an answer rather than staying up for ever.
    await expect(page.getByRole('status', { name: 'Loading' })).toBeHidden();
  });
});
