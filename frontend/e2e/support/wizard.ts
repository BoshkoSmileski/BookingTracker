import { expect, type Page } from '@playwright/test';

/**
 * Driving the booking sheet through a real browser.
 *
 * Every locator here is semantic - a role and an accessible name - because those
 * are the things the app promises and the frontend suite already pins. The one
 * exception is `[role="grid"] button:not([disabled])` for "a bookable day": a
 * calendar cell's accessible name is a locale-formatted date
 * (`date.toLocaleDateString(undefined, …)`), so naming one would hardcode both a
 * locale and a date - two things this suite deliberately avoids.
 * Enabled-ness *is* the semantic here: `DateCalendar` disables every day the
 * server did not offer, so "the first enabled gridcell" is exactly "the first
 * day this organizer is free".
 */

/** Picks the first day the calendar offers, and returns the heading that then names it. */
export async function pickFirstAvailableDate(page: Page): Promise<void> {
  const firstAvailable = page.locator('[role="grid"] button:not([disabled])').first();
  await expect(firstAvailable).toBeVisible();
  await firstAvailable.click();

  // The times panel heading changes from "Available times" to "Times on <day>"
  // once a date is chosen - the app's own confirmation that the click landed,
  // so nothing here waits on a timer.
  await expect(page.getByRole('heading', { name: /^Times on / })).toBeVisible();
}

/** The times offered for the chosen day. A `role="group"` of `aria-pressed` buttons, not a listbox. */
export function timeSlots(page: Page) {
  return page.getByRole('group', { name: 'Available times' }).getByRole('button');
}

/**
 * Date + time, which is one screen and one decision in this wizard.
 * Choosing a time commits and advances, so this lands on the details step.
 */
export async function pickFirstSlot(page: Page): Promise<string> {
  await pickFirstAvailableDate(page);

  const first = timeSlots(page).first();
  await expect(first).toBeVisible();
  // The visible label is the ORGANIZER's wall clock (see TimeSlotList) - the
  // fact the wizard once got wrong. Returned so callers can assert that the same
  // reading survives to the confirmation.
  const label = (await first.innerText()).split('\n')[0].trim();
  await first.click();

  await expect(page.getByRole('heading', { name: 'Your details' })).toBeVisible();
  return label;
}

export async function fillGuestDetails(
  page: Page,
  guest: { name: string; email: string },
): Promise<void> {
  await page.getByLabel('Name', { exact: true }).fill(guest.name);
  await page.getByLabel('Email', { exact: true }).fill(guest.email);
}
