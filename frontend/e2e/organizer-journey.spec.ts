import { expect, test } from '@playwright/test';
import { unique } from './support/api';
import { fillGuestDetails, pickFirstSlot } from './support/wizard';

/**
 * The whole product, once, in a real browser: an organizer signs up and
 * publishes a page, and a guest books on it.
 *
 * This is the only test in the suite that creates its fixtures through the UI.
 * Every other spec sets up over the API, because clicking a create form a second
 * time proves nothing new - whereas here the clicking IS the claim. It is also
 * the only test that crosses both halves of the product in one browser context,
 * which is exactly what neither jsdom (no server) nor `WebApplicationFactory`
 * (no browser) can do: the slug an organizer is shown has to be the slug a guest
 * can reach, the schedule created behind the create form has to be the schedule
 * the calendar draws, and the time on the button has to be the time on the
 * confirmation.
 */
test('an organizer signs up, publishes a page, and a guest books on it', async ({ page }) => {
  const email = `${unique('journey')}@bookingtracker.test`;
  const pageTitle = `Journey ${unique('call')}`;
  const guest = { name: 'Ada Lovelace', email: 'ada@guest.test' };

  // ---- Organizer: register -------------------------------------------------
  await page.goto('/register');
  await page.getByLabel('Name').fill('Journey Organizer');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill('E2ePassw0rd!');
  await page.getByRole('button', { name: 'Create account' }).click();

  // A brand-new organizer owns no booking pages, and DashboardHomePage redirects
  // an empty list to the create form rather than showing an empty workspace.
  await expect(page).toHaveURL(/\/dashboard\/new$/);
  await expect(page.getByRole('heading', { name: 'Create a booking page' })).toBeVisible();

  // The create form states what a first schedule will be, rather than warning
  // that none exists - the seeded Monday-Friday 09:00-17:00 asserted below.
  await expect(page.getByText(/Monday.{0,3}Friday, 09:00.{0,3}17:00/)).toBeVisible();

  // ---- Organizer: create the booking page ----------------------------------
  await page.getByLabel('Title').fill(pageTitle);
  await page.getByRole('button', { name: 'Create booking page' }).click();

  // Lands on the page's OWN dashboard, never on a settings screen.
  await expect(page).toHaveURL(/\/dashboard\/[0-9a-f-]{36}$/i);
  await expect(page.getByRole('heading', { name: 'Sessions' })).toBeVisible();

  // "Live" is the claim that the seeded schedule exists and is bookable - the
  // panel only renders this branch once it has confirmed bookable availability.
  const createdPanel = page.getByRole('region', { name: 'Booking page created' });
  await expect(createdPanel.getByRole('heading', { name: 'Your booking page is live' })).toBeVisible();

  // The public URL, on the screen the organizer actually lands on. Read from the
  // page rather than assembled here, so the test cannot agree with itself about
  // the slug the server generated.
  const publicUrl = (await createdPanel.locator('code').innerText()).trim();
  expect(publicUrl).toContain('/book/');

  // ---- Organizer: the default working hours really are there ---------------
  await page.getByRole('link', { name: 'Working hours' }).first().click();
  await expect(page.getByRole('heading', { name: 'Working hours' })).toBeVisible();
  await expect(page.getByRole('checkbox', { name: 'Monday' })).toBeChecked();
  await expect(page.getByLabel('Monday interval 1 start')).toHaveValue('09:00');
  await expect(page.getByLabel('Monday interval 1 end')).toHaveValue('17:00');
  // The weekend is deliberately closed, which is the half of the default that
  // proves it was seeded rather than "everything switched on".
  await expect(page.getByRole('checkbox', { name: 'Saturday' })).not.toBeChecked();

  // ---- Guest: book on that page --------------------------------------------
  await page.goto(publicUrl);

  await expect(page.getByRole('heading', { level: 1, name: pageTitle })).toBeVisible();
  await expect(page.getByText('30 minutes')).toBeVisible();

  const chosenTime = await pickFirstSlot(page);

  await fillGuestDetails(page, guest);
  await page.getByRole('button', { name: 'Continue' }).click();

  await expect(page.getByRole('heading', { name: 'Check and confirm' })).toBeVisible();
  await expect(page.getByRole('definition').filter({ hasText: guest.email })).toBeVisible();
  await page.getByRole('button', { name: 'Confirm booking' }).click();

  // ---- Guest: the confirmation ---------------------------------------------
  await expect(page.getByRole('heading', { name: 'Booking confirmed' })).toBeVisible();
  await expect(page.getByText(/Reference/)).toBeVisible();

  // The time the guest clicked is the time the booking says it is. Both readings
  // are the organizer's wall clock; the wizard once showed the visitor's zone on
  // the button and the organizer's on the next screen, so this pins that the two
  // agree end to end through a real submit.
  // `main header` rather than `getByRole('banner')`: a <header> scoped inside
  // <main> is deliberately not a banner landmark, and BookingHeader lives inside
  // the booking sheet.
  await expect(page.locator('main header')).toContainText(chosenTime);

  // The way back to the booking, offered rather than left to the email.
  await expect(page.getByRole('link', { name: 'Manage booking' })).toBeVisible();
});
