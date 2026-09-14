import { screen, waitForElementToBeRemoved } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { BookingInstructionsPage } from '../BookingInstructionsPage';
import { BookingLimitsPage } from '../BookingLimitsPage';
import { BookingPageDetailsPage } from '../BookingPageDetailsPage';
import { CalendarIntegrationPage } from '../CalendarIntegrationPage';
import { RegisterPage } from '../RegisterPage';
import { SchedulingSettingsPage } from '../SchedulingSettingsPage';
import { api } from '../../lib/api';
import { bookingPageDetail, bookingPageSummary, calendarConnection } from '../../test/factories';
import { renderWithProviders } from '../../test/render';
import { resetAuthState } from '../../test/authContextMock';

vi.mock('../../contexts/AuthContext', () => import('../../test/authContextMock'));

/**
 * `<Field>` on the screens that adopted it.
 *
 * `Field.test.tsx` pins the primitive; this pins that these pages are actually
 * wired to it, which is a separate claim and the one nothing else in the suite
 * could make - several of them had no test file at all before this, so a
 * migration that quietly labelled a wrapper instead of the control would have
 * gone unnoticed. Same reasoning as the unsaved-changes wiring tests one file over.
 *
 * The first three previously re-declared `` `${LABEL} ${LABEL_GAP}` `` locally
 * rather than using `FieldLabel`, so what is really being asserted there is
 * that removing those copies changed nothing a user can observe.
 *
 * The two COMPOSITE fields below are the ones that matter most. Both put a
 * second element next to the control - a "minutes before" unit, a submit
 * button - and both are the case `<Field>`'s render prop exists for: a cloning
 * API would have put the id and the ARIA on the flex wrapper, producing a
 * label that points at a non-control with nothing on screen to show for it.
 * `tagName` is asserted for exactly that reason: `getByLabelText` resolving at
 * all is not enough, it has to resolve to the input.
 */

const PAGE_ID = '11111111-1111-1111-1111-111111111111';

beforeEach(() => {
  resetAuthState();
});

/** No hint and no error, so neither ARIA attribute should be there at all. */
function expectPlainField(control: HTMLElement) {
  expect(control).not.toHaveAttribute('aria-describedby');
  expect(control).not.toHaveAttribute('aria-invalid');
}

describe('BookingLimitsPage — fields', () => {
  it('labels all three limits, and describes none of them', async () => {
    vi.spyOn(api.organizer, 'getBookingPage').mockResolvedValue(
      bookingPageDetail({ minNoticeMinutes: 60, maxBookingWindowDays: 30, maxBookingsPerDay: 4 }),
    );
    renderWithProviders(<BookingLimitsPage />, {
      path: '/dashboard/:pageId/settings/limits',
      route: `/dashboard/${PAGE_ID}/settings/limits`,
    });

    const notice = await screen.findByLabelText('Minimum notice (minutes)');
    expect(notice).toHaveValue(60);
    expect(screen.getByLabelText('Max booking window (days)')).toHaveValue(30);
    expect(screen.getByLabelText('Max bookings per day')).toHaveValue(4);
    expectPlainField(notice);
  });

  it('still edits: the control the label points at is the one that takes input', async () => {
    vi.spyOn(api.organizer, 'getBookingPage').mockResolvedValue(bookingPageDetail());
    const { user } = renderWithProviders(<BookingLimitsPage />, {
      path: '/dashboard/:pageId/settings/limits',
      route: `/dashboard/${PAGE_ID}/settings/limits`,
    });

    const field = await screen.findByLabelText('Max bookings per day');
    await user.type(field, '7');

    expect(field).toHaveValue(7);
  });
});

describe('SchedulingSettingsPage — fields', () => {
  it('labels the duration and both buffers', async () => {
    vi.spyOn(api.organizer, 'getMyBookingPages').mockResolvedValue([
      bookingPageSummary({ id: PAGE_ID, durationMinutes: 45, bufferBeforeMinutes: 5, bufferAfterMinutes: 10 }),
    ]);
    renderWithProviders(<SchedulingSettingsPage />, {
      path: '/dashboard/:pageId/settings/scheduling',
      route: `/dashboard/${PAGE_ID}/settings/scheduling`,
    });

    const duration = await screen.findByLabelText('Appointment duration (minutes)');
    expect(duration).toHaveValue(45);
    expect(screen.getByLabelText('Buffer before (minutes)')).toHaveValue(5);
    expect(screen.getByLabelText('Buffer after (minutes)')).toHaveValue(10);
    expectPlainField(duration);
  });
});

describe('CalendarIntegrationPage — composite fields', () => {
  async function renderLoaded() {
    vi.spyOn(api.calendar, 'getConnection').mockResolvedValue(calendarConnection());
    const result = renderWithProviders(<CalendarIntegrationPage />, {
      path: '/dashboard/:pageId/settings/calendar',
      route: `/dashboard/${PAGE_ID}/settings/calendar`,
    });
    await waitForElementToBeRemoved(() => screen.queryByLabelText('Loading'));
    return result;
  }

  it('points "Default reminder" at the input, not at the row holding its unit', async () => {
    await renderLoaded();

    const reminder = screen.getByLabelText('Default reminder');
    expect(reminder.tagName).toBe('INPUT');
    expect(reminder).toHaveAttribute('type', 'number');
    // The unit is a sibling inside the same row, and is not part of the control.
    expect(reminder.parentElement).toHaveTextContent('minutes before');
    expectPlainField(reminder);
  });

  it('still edits through the labelled control', async () => {
    const { user } = await renderLoaded();

    const reminder = screen.getByLabelText('Default reminder');
    await user.clear(reminder);
    await user.type(reminder, '45');

    expect(reminder).toHaveValue(45);
  });

  it('labels the visibility select', async () => {
    await renderLoaded();

    const visibility = screen.getByLabelText('Event visibility');
    expect(visibility.tagName).toBe('SELECT');
    expectPlainField(visibility);
  });
});

describe('BookingInstructionsPage — composite field', () => {
  it('points "Instruction" at the input beside the submit button, and describes it', async () => {
    vi.spyOn(api.organizer, 'getBookingPage').mockResolvedValue(bookingPageDetail({ instructions: [] }));
    renderWithProviders(<BookingInstructionsPage />, {
      path: '/dashboard/:pageId/settings/instructions',
      route: `/dashboard/${PAGE_ID}/settings/instructions`,
    });

    const input = await screen.findByLabelText('Instruction');
    expect(input.tagName).toBe('INPUT');

    // The hint spelled `META` out by hand and described nothing.
    const hint = screen.getByText(/nothing for them to fill in/i);
    expect(hint.id).not.toBe('');
    expect(input).toHaveAttribute('aria-describedby', hint.id);
    expect(input).not.toHaveAttribute('aria-invalid');

    // The button shares the row; it is not what the label resolves to.
    expect(screen.getByRole('button', { name: /add instruction/i })).not.toBe(input);
  });
});

describe('RegisterPage — fields', () => {
  it('ties the password hint to the password input', async () => {
    renderWithProviders(<RegisterPage />, { route: '/register' });

    const password = screen.getByLabelText('Password');
    const hint = screen.getByText('At least 8 characters.');
    expect(password).toHaveAttribute('aria-describedby', hint.id);
    // Name and email are required and carry no hint, so neither attribute.
    expectPlainField(screen.getByLabelText('Name'));
    expectPlainField(screen.getByLabelText('Email'));
  });
});

describe('BookingPageDetailsPage — fields', () => {
  it('labels the title, and says the description is optional', async () => {
    vi.spyOn(api.organizer, 'getBookingPage').mockResolvedValue(
      bookingPageDetail({ title: 'Discovery call', description: 'A quick chat' }),
    );
    renderWithProviders(<BookingPageDetailsPage />, {
      path: '/dashboard/:pageId/settings/details',
      route: `/dashboard/${PAGE_ID}/settings/details`,
    });

    // Title keeps its bare name - required by default, marked only on the
    // exceptions - which is also what the unsaved-changes guard test queries by.
    const title = await screen.findByLabelText('Title');
    expect(title).toHaveValue('Discovery call');
    expectPlainField(title);

    // The handler sends `description || null`, so it genuinely is optional and
    // nothing on this screen used to say so.
    const description = screen.getByLabelText('Description (optional)');
    expect(description.tagName).toBe('TEXTAREA');
    expect(description).toHaveValue('A quick chat');
  });
});
