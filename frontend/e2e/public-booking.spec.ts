import { addFormField, createBookingPage, unique } from './support/api';
import { API_URL } from './support/env.mjs';
import { expect, test } from './support/fixtures';
import { fillGuestDetails, pickFirstAvailableDate, pickFirstSlot, timeSlots } from './support/wizard';

/**
 * The guest flow, anonymously, against a real API.
 *
 * Deliberately not a re-test of the booking rules - slot generation, conflicts,
 * buffers and notice periods all belong to the unit suite, which is faster and
 * far more precise. What is here is what only a browser can show: that the
 * wizard's three steps hold state across a real submit, that a required custom
 * question reaches the server as a `custom:{fieldId}` answer and comes back
 * attached to the booking, and that the guest is never asked to sign in.
 */

test.describe('public booking', () => {
  test('a guest books through the wizard without an account', async ({ page, bookingPage }) => {
    const guest = { name: 'Grace Hopper', email: 'grace@guest.test' };

    await page.goto(`/book/${bookingPage.slug}`);

    // Anonymous: no redirect to /login, and the sheet renders rather than the
    // organizer shell.
    await expect(page).toHaveURL(new RegExp(`/book/${bookingPage.slug}$`));
    await expect(page.getByRole('heading', { level: 1, name: bookingPage.title })).toBeVisible();

    // The organizer's zone, named. Everything this API hands a visitor is on
    // that clock, so the header has to say which one it is - the page was
    // created with timeZoneId "UTC".
    await expect(page.locator('main header')).toContainText('UTC');

    // Before a date is picked the times panel says what to do, not "no times".
    await expect(page.getByText('Pick a date')).toBeVisible();

    await pickFirstAvailableDate(page);
    await expect(timeSlots(page).first()).toBeVisible();

    const chosenTime = await pickFirstSlot(page);

    // The chosen slot is absorbed into the header and travels with the guest.
    await expect(page.locator('main header')).toContainText(chosenTime);

    // Continue is gated until the required fields are there - the client-side
    // convenience in front of a rule the server still enforces.
    const continueButton = page.getByRole('button', { name: 'Continue' });
    await expect(continueButton).toBeDisabled();
    await expect(page.getByText(/Still needed: .*Name.*Email/)).toBeVisible();

    await fillGuestDetails(page, guest);
    await expect(continueButton).toBeEnabled();
    await continueButton.click();

    await expect(page.getByRole('heading', { name: 'Check and confirm' })).toBeVisible();
    await page.getByRole('button', { name: 'Confirm booking' }).click();

    await expect(page.getByRole('heading', { name: 'Booking confirmed' })).toBeVisible();
    await expect(page.locator('main header')).toContainText(chosenTime);
  });

  test('a required custom question is enforced and its answer survives the booking', async ({
    page,
    request,
    organizer,
  }) => {
    // Its own page rather than the shared fixture: adding a required question
    // changes what every other test on that page would have to fill in.
    const withQuestion = await createBookingPage(request, organizer, {
      title: `Questions ${unique('page')}`,
    });
    const label = 'What would you like to discuss?';
    await addFormField(request, organizer, withQuestion.id, { label, isRequired: true });

    await page.goto(`/book/${withQuestion.slug}`);
    await pickFirstSlot(page);

    await fillGuestDetails(page, { name: 'Alan Turing', email: 'alan@guest.test' });

    // Named, not merely counted: the details step lists the fields actually
    // missing rather than restating the rule.
    await expect(page.getByText(new RegExp(`Still needed: .*${label}`))).toBeVisible();
    await expect(page.getByRole('button', { name: 'Continue' })).toBeDisabled();

    await page.getByLabel(label).fill('Rebuilding the projection from the log');
    await page.getByRole('button', { name: 'Continue' }).click();

    // The answer is reviewed in the same summary list as the rest of the
    // booking, keyed by the organizer's own label.
    await expect(page.getByRole('term').filter({ hasText: label })).toBeVisible();
    await expect(
      page.getByRole('definition').filter({ hasText: 'Rebuilding the projection from the log' }),
    ).toBeVisible();

    await page.getByRole('button', { name: 'Confirm booking' }).click();
    await expect(page.getByRole('heading', { name: 'Booking confirmed' })).toBeVisible();

    // And it really reached the server. `BookingSessionAnswers` is written only
    // by `BookingSession.Apply` in response to a FieldChanged event named
    // `custom:{fieldId}` - so the answer being on the organizer's own read of
    // the session proves the whole browser -> tracker -> events -> projection
    // path, and that the `customFieldName` encoder on each side agrees.
    const sessions = await request.get(
      `${API_URL}/api/organizer/booking-pages/${withQuestion.id}/sessions?status=Submitted`,
      { headers: { Authorization: `Bearer ${organizer.accessToken}` } },
    );
    expect(sessions.ok()).toBeTruthy();
    const [session] = await sessions.json();
    expect(session.answers).toHaveLength(1);
    expect(session.answers[0].value).toBe('Rebuilding the projection from the log');
  });

  test('an unknown slug shows the app\'s own copy, not the API\'s exception', async ({ page }) => {
    await page.goto('/book/no-such-booking-page');

    await expect(page.getByRole('heading', { name: "This booking page isn't available" })).toBeVisible();
    await expect(
      page.getByText('The link may be wrong, or the organizer may have turned this page off.'),
    ).toBeVisible();

    // The API's own 404 title names an internal type. A guest must never read it.
    await expect(page.getByText(/BookingPage with key/)).toBeHidden();
  });
});
