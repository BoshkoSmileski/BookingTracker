import { screen, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { BookingPage } from '../BookingPage';
import { ApiError, NetworkError, api } from '../../lib/api';
import { bookingConfirmation, bookingFormField, bookingPage, bookingSession } from '../../test/factories';
import { renderWithProviders } from '../../test/render';
import type { AvailableSlotDto, BookingPageDto } from '../../lib/types';

/**
 * The guest journey, through the real orchestrator: choose a slot, give your
 * details, confirm, and land on a confirmation.
 *
 * Driven through the UI rather than by poking state, so it also covers what the
 * three-step rebuild is actually for - that the calendar and the times are one
 * screen, that the header keeps the choice visible afterwards, and that a
 * booking conflict has somewhere to go.
 *
 * `api` methods are spied on individually rather than the module being mocked,
 * so `errorMessage`, `ApiError` and `NetworkError` stay real.
 */

/**
 * A date the calendar will actually offer: ten days out, but never past the end
 * of the month the wizard opens on.
 *
 * Clock-derived rather than hardcoded - but clock-derived
 * is not the same as deterministic, which is what that rule is really after.
 * `BookingPage` opens `monthCursor` on the first of the CURRENT month, and
 * `DateCalendar.isSelectable` is `inMonth && available` - so a day belonging to
 * the next month still renders, as a disabled trailing cell. A bare `today + 10`
 * lands on one for the last ~10 days of every month - 32.9% of all days, measured
 * over 2024-2031 - and because the cell exists, the click is a silent no-op rather
 * than an error: the failure surfaces a step later, at the time button. Clamping
 * to the month end keeps the target inside the grid the wizard actually shows, on
 * every day of the year.
 *
 * Both constructors yield local midnight, so no clamping of the time is needed.
 *
 * On the one day a month the clamp yields today itself, that is still correct:
 * production deliberately does NOT exclude today or the past from
 * `isSelectable` - the server has already applied the same-day cutoff and the
 * minimum notice on the organizer's clock. See `DateCalendar`'s own note and
 * The server owns that decision; a client-side date guard there was a real bug.
 */
const TARGET = (() => {
  const now = new Date();
  const endOfMonth = new Date(now.getFullYear(), now.getMonth() + 1, 0);
  const tenDaysOut = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 10);
  return tenDaysOut <= endOfMonth ? tenDaysOut : endOfMonth;
})();
const TARGET_KEY = `${TARGET.getFullYear()}-${String(TARGET.getMonth() + 1).padStart(2, '0')}-${String(TARGET.getDate()).padStart(2, '0')}`;
const TARGET_LABEL = TARGET.toLocaleDateString(undefined, {
  weekday: 'long', day: 'numeric', month: 'long', year: 'numeric',
});

function slot(startTime: string, endTime: string): AvailableSlotDto {
  return {
    localDate: TARGET_KEY,
    localStartTime: startTime,
    localEndTime: endTime,
    // The instant is only used for the "your time" reading and the calendar
    // file; the exact value does not matter to these assertions.
    startUtc: `${TARGET_KEY}T07:00:00Z`,
    endUtc: `${TARGET_KEY}T07:30:00Z`,
  };
}

const SLOTS = [slot('09:00:00', '09:30:00'), slot('10:00:00', '10:30:00')];

/** A custom question's id, which is also half the `custom:{id}` server error key. */
const FIELD_ID = '2f1c9d40-0000-0000-0000-000000000001';

function stubApi(page: BookingPageDto = bookingPage()) {
  vi.spyOn(api, 'getBookingPage').mockResolvedValue(page);
  vi.spyOn(api, 'startSession').mockResolvedValue(
    bookingSession({ status: 'Active', name: null, email: null, selectedDate: null, selectedTime: null, submittedAt: null }),
  );
  vi.spyOn(api, 'getAvailableSlots').mockResolvedValue(SLOTS);
  vi.spyOn(api, 'appendEvents').mockImplementation(async () =>
    bookingSession({ status: 'Active', submittedAt: null }),
  );
  return page;
}

const renderPage = () =>
  renderWithProviders(<BookingPage />, { route: '/book/demo-30-min-meeting', path: '/book/:slug' });

/** Walks the wizard as a guest does: pick the day, pick the time, fill in details. */
async function reachDetails(user: ReturnType<typeof renderWithProviders>['user']) {
  await user.click(await screen.findByRole('gridcell', { name: TARGET_LABEL }));
  await user.click(screen.getByRole('button', { name: /^09:00/ }));
  await screen.findByLabelText('Name');
}

async function fillDetails(user: ReturnType<typeof renderWithProviders>['user']) {
  await user.type(screen.getByLabelText('Name'), 'Jane Doe');
  await user.type(screen.getByLabelText('Email'), 'jane@example.com');
  await user.click(screen.getByRole('button', { name: 'Continue' }));
  await screen.findByRole('button', { name: 'Confirm booking' });
}

describe('BookingPage — opening the page', () => {
  beforeEach(() => stubApi());

  it('shows the sheet\'s own shape while loading, not an empty screen', () => {
    renderPage();

    expect(screen.getByRole('status', { name: 'Loading' })).toBeInTheDocument();
  });

  it('opens straight onto the calendar, with no service to select first', async () => {
    renderPage();

    expect(await screen.findByRole('grid', { name: /choose a date/i })).toBeInTheDocument();
    // The step that asked a guest to pick from one option is gone.
    expect(screen.queryByText(/select a service/i)).not.toBeInTheDocument();
  });

  it('states what is being booked, for how long, where and in whose time zone', async () => {
    renderPage();

    expect(await screen.findByRole('heading', { level: 1, name: '30 Minute Meeting' })).toBeInTheDocument();
    expect(screen.getByText('Demo Organizer')).toBeInTheDocument();
    expect(screen.getByText('30 minutes')).toBeInTheDocument();
    expect(screen.getByText('In person')).toBeInTheDocument();
    expect(screen.getByText(/Europe\/Skopje \(GMT[+-]\d/)).toBeInTheDocument();
  });
});

describe('BookingPage — choosing a slot', () => {
  beforeEach(() => stubApi());

  it('records the slot on the organizer\'s clock the moment it is picked', async () => {
    const { user } = renderPage();
    await reachDetails(user);

    // DateSelected + TimeSelected, with the wall-clock values the backend
    // stores - unchanged from before the rebuild, which is the point.
    expect(api.appendEvents).toHaveBeenCalledWith(
      expect.any(String),
      expect.arrayContaining([
        expect.objectContaining({ eventType: 'DateSelected', newValue: TARGET_KEY }),
        expect.objectContaining({ eventType: 'TimeSelected', newValue: '09:00:00' }),
      ]),
    );
  });

  it('keeps the chosen slot visible in the header on every later step', async () => {
    const { user } = renderPage();
    await reachDetails(user);

    // Still on screen after leaving the calendar behind.
    expect(screen.getByText(new RegExp(TARGET_LABEL.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')))).toBeInTheDocument();
    expect(screen.getByText(/09:00 – 09:30/)).toBeInTheDocument();
  });

  it('lets the guest change the slot from a later step without walking backwards', async () => {
    const { user } = renderPage();
    await reachDetails(user);

    await user.click(screen.getByRole('button', { name: 'Change the selected date and time' }));

    expect(await screen.findByRole('grid', { name: /choose a date/i })).toBeInTheDocument();
  });
});

describe('BookingPage — details', () => {
  beforeEach(() => stubApi());

  it('will not continue until the fields a booking cannot do without are filled', async () => {
    const { user } = renderPage();
    await reachDetails(user);

    expect(screen.getByRole('button', { name: 'Continue' })).toBeDisabled();
    expect(screen.getByText(/still needed: name, email/i)).toBeInTheDocument();
  });

  it('asks the organizer\'s own questions alongside the standard fields', async () => {
    stubApi(bookingPage({ formFields: [bookingFormField({ id: 'f1', label: 'Company', isRequired: true })] }));
    const { user } = renderPage();
    await reachDetails(user);

    expect(screen.getByLabelText('Company')).toBeInTheDocument();
    // A required question gates Continue exactly as name and email do.
    await user.type(screen.getByLabelText('Name'), 'Jane Doe');
    await user.type(screen.getByLabelText('Email'), 'jane@example.com');
    expect(screen.getByRole('button', { name: 'Continue' })).toBeDisabled();
    expect(screen.getByText(/still needed: company/i)).toBeInTheDocument();
  });
});

describe('BookingPage — confirming', () => {
  beforeEach(() => stubApi());

  it('shows the whole booking back, with the meeting and the time zone', async () => {
    const { user } = renderPage();
    await reachDetails(user);
    await fillDetails(user);

    expect(screen.getByText('Jane Doe')).toBeInTheDocument();
    expect(screen.getByText('jane@example.com')).toBeInTheDocument();
    // The time zone row used to name the *visitor's* zone beside a time on the
    // organizer's clock.
    expect(screen.getAllByText(/Europe\/Skopje \(GMT[+-]\d/).length).toBeGreaterThan(0);
  });

  it('names the action rather than saying "Continue"', async () => {
    const { user } = renderPage();
    await reachDetails(user);
    await fillDetails(user);

    expect(screen.getByRole('button', { name: 'Confirm booking' })).toBeInTheDocument();
  });

  it('jumps straight back to the calendar from the summary, not through the details step', async () => {
    const { user } = renderPage();
    await reachDetails(user);
    await fillDetails(user);

    await user.click(screen.getByRole('button', { name: 'Edit the date and time' }));

    expect(await screen.findByRole('grid', { name: /choose a date/i })).toBeInTheDocument();
  });

  it('confirms the booking and lands on a confirmation', async () => {
    vi.spyOn(api, 'submitSession').mockResolvedValue(bookingConfirmation());
    const { user } = renderPage();
    await reachDetails(user);
    await fillDetails(user);

    await user.click(screen.getByRole('button', { name: 'Confirm booking' }));

    expect(await screen.findByRole('heading', { name: /booking confirmed/i })).toBeInTheDocument();
    // Submitting QUEUES the confirmation; EmailQueueProcessor sends it seconds
    // later, out of this request. "We've emailed the details" was a claim the
    // app could not make at this moment.
    expect(screen.getByText(/on its way to/i)).toBeInTheDocument();
    expect(screen.queryByText(/we've emailed/i)).not.toBeInTheDocument();
    expect(screen.getByText('BK-12345')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Manage booking' })).toHaveAttribute('href', '/manage/public-token-abc');
  });
});

describe('BookingPage — when things go wrong', () => {
  it('offers a way out of a slot taken while the guest was filling the form', async () => {
    stubApi();
    vi.spyOn(api, 'submitSession').mockRejectedValue(
      new ApiError(409, { title: 'This time slot is no longer available. Please choose another time.' }),
    );
    const { user } = renderPage();
    await reachDetails(user);
    await fillDetails(user);

    await user.click(screen.getByRole('button', { name: 'Confirm booking' }));

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText(/no longer available/i)).toBeInTheDocument();
    // The message tells the guest to choose another time, so the screen has to
    // let them - it used to leave them on a dead end.
    await user.click(within(alert).getByRole('button', { name: /choose another time/i }));
    expect(await screen.findByRole('grid', { name: /choose a date/i })).toBeInTheDocument();
  });

  it('tells the guest which question is missing instead of "Validation failed"', async () => {
    // REGRESSION: SubmitBookingSessionCommandHandler keys a missing required
    // answer by `custom:{fieldId}` precisely so the wizard can point at it, and
    // the response's title is the literal string "Validation failed". Nothing
    // read the field map, so that care produced two useless words for a guest
    // who could not see what to fix.
    const field = bookingFormField({ id: FIELD_ID, label: 'Company', isRequired: true });
    stubApi(bookingPage({ formFields: [field] }));
    vi.spyOn(api, 'submitSession').mockRejectedValue(
      new ApiError(400, {
        title: 'Validation failed',
        errors: { [`custom:${FIELD_ID}`]: ['Company is required.'] },
      }),
    );
    const { user } = renderPage();
    await reachDetails(user);
    // Answered locally, so the client-side gate lets the guest through - which
    // is the state this is actually reachable in: the answer's flush failed, or
    // the organizer added the question after the session started.
    await user.type(screen.getByLabelText('Company'), 'Acme');
    await fillDetails(user);

    await user.click(screen.getByRole('button', { name: 'Confirm booking' }));

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText('Company is required.')).toBeInTheDocument();
    expect(screen.queryByText(/^Validation failed$/)).not.toBeInTheDocument();
  });

  it('puts that message beside the question once the guest goes back to it', async () => {
    const field = bookingFormField({ id: FIELD_ID, label: 'Company', isRequired: true });
    stubApi(bookingPage({ formFields: [field] }));
    vi.spyOn(api, 'submitSession').mockRejectedValue(
      new ApiError(400, {
        title: 'Validation failed',
        errors: { [`custom:${FIELD_ID}`]: ['Company is required.'] },
      }),
    );
    const { user } = renderPage();
    await reachDetails(user);
    await user.type(screen.getByLabelText('Company'), 'Acme');
    await fillDetails(user);
    await user.click(screen.getByRole('button', { name: 'Confirm booking' }));

    // The inputs live a step back, so the notice has to offer the way there.
    const alert = await screen.findByRole('alert');
    await user.click(within(alert).getByRole('button', { name: /go back to your details/i }));

    const input = await screen.findByLabelText('Company');
    const message = screen.getByText('Company is required.');
    expect(input).toHaveAttribute('aria-invalid', 'true');
    expect(input).toHaveAttribute('aria-describedby', message.id);
  });

  it('reports an ordinary submit failure without offering a time change', async () => {
    stubApi();
    vi.spyOn(api, 'submitSession').mockRejectedValue(new NetworkError());
    const { user } = renderPage();
    await reachDetails(user);
    await fillDetails(user);

    await user.click(screen.getByRole('button', { name: 'Confirm booking' }));

    const alert = await screen.findByRole('alert');
    expect(within(alert).getByText(/could not reach the booking service/i)).toBeInTheDocument();
    expect(within(alert).queryByRole('button', { name: /choose another time/i })).not.toBeInTheDocument();
  });

  it('explains an unreachable server rather than blaming the link', async () => {
    vi.spyOn(api, 'getBookingPage').mockRejectedValue(new NetworkError());
    renderPage();

    expect(await screen.findByText(/could not reach the booking service/i)).toBeInTheDocument();
  });

  it('explains a booking page that does not exist without quoting the backend at the guest', async () => {
    // Found by running the app: the API's real 404 title names an internal
    // type - "BookingPage with key 'no-such-page' was not found." - and it was
    // being printed verbatim to whoever followed a bad link.
    vi.spyOn(api, 'getBookingPage').mockRejectedValue(
      new ApiError(404, { title: "BookingPage with key 'no-such-page' was not found." }),
    );
    renderPage();

    expect(await screen.findByRole('heading', { name: /isn't available/i })).toBeInTheDocument();
    expect(screen.getByText(/the link may be wrong/i)).toBeInTheDocument();
    expect(screen.queryByText(/BookingPage with key/)).not.toBeInTheDocument();
  });
});
