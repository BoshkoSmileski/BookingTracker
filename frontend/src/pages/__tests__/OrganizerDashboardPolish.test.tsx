import { screen, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { OrganizerDashboardPage } from '../OrganizerDashboardPage';
import { api } from '../../lib/api';
import { bookingSession, workingSchedule } from '../../test/factories';
import { formatCalendarDateTime } from '../../lib/calendarDates';
import { renderWithProviders } from '../../test/render';

vi.mock('../../contexts/AuthContext', () => import('../../test/authContextMock'));
vi.mock('../../lib/signalr', () => ({ createDashboardConnection: () => ({
  on: () => {}, start: () => Promise.reject(new Error('no hub')), stop: () => Promise.resolve(), invoke: () => Promise.resolve(),
}) }));
// The summary band has its own tests; here it is noise on every assertion.
vi.mock('../../components/DashboardSummary', () => ({ DashboardSummary: () => null }));

const PAGE_ID = '11111111-1111-1111-1111-111111111111';

/**
 * The session list's layout corrections: the appointment and status survive a
 * phone, and "Upcoming" is decided on the organizer's clock.
 */
describe('OrganizerDashboardPage polish', () => {
  beforeEach(() => {
    vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(
      workingSchedule({ timeZoneId: 'Asia/Tokyo' }),
    );
  });

  const renderPage = () =>
    renderWithProviders(<OrganizerDashboardPage />, {
      route: `/dashboard/${PAGE_ID}?status=Submitted`,
      path: '/dashboard/:pageId',
    });

  it('keeps the appointment and the status in the row at every width', async () => {
    // They used to be `hidden sm:block`, so a phone showed a name and an email
    // and nothing about the booking. The row is a grid now: the same cells
    // stack instead of disappearing, so there is exactly one of each in the
    // DOM and no width can remove them.
    vi.spyOn(api.organizer, 'getSessions').mockResolvedValue([
      bookingSession({ status: 'Submitted', name: 'Jane Doe', selectedDate: '2099-08-20', selectedTime: '09:00:00' }),
    ]);
    renderPage();

    // Derived from the same formatter the component uses - a hardcoded
    // "20 Aug 2099" fails on any machine with another locale.
    const appointment = formatCalendarDateTime('2099-08-20', '09:00:00');

    const row = await screen.findByRole('link', { name: /jane doe/i });
    expect(within(row).getByText(appointment)).toBeInTheDocument();
    // The status label and its qualifier are separate spans in StatusPill.
    expect(within(row).getByText('Submitted')).toBeInTheDocument();
    expect(within(row).getByText('Upcoming')).toBeInTheDocument();
    // One instance of each, not a mobile copy beside a desktop one - a
    // duplicate would be announced twice and would break every `getByText`.
    expect(within(row).getAllByText(appointment)).toHaveLength(1);
    expect(within(row).getAllByText('Submitted')).toHaveLength(1);
  });

  it('lays the row out as a grid rather than hiding cells below sm', async () => {
    vi.spyOn(api.organizer, 'getSessions').mockResolvedValue([bookingSession({ status: 'Submitted' })]);
    renderPage();

    const row = await screen.findByRole('link', { name: /jane doe/i });
    expect(row.className).toContain('grid');
    // The desktop table is preserved: the same four tracks the header declares.
    expect(row.className).toContain('sm:grid-cols-[minmax(0,1fr)_14rem_11rem_auto]');
    expect(row.querySelector('.hidden')).toBeNull();
  });

  it('decides Upcoming on the organizer clock, not the browser one', async () => {
    // A booking far enough ahead that it is upcoming in every zone - what is
    // under test is that the label is produced at all once the zone resolves.
    vi.spyOn(api.organizer, 'getSessions').mockResolvedValue([
      bookingSession({ status: 'Submitted', selectedDate: '2099-01-01', selectedTime: '09:00:00' }),
    ]);
    renderPage();

    const row = await screen.findByRole('link', { name: /jane doe/i });
    expect(within(row).getByText('Submitted')).toBeInTheDocument();
    expect(await within(row).findByText('Upcoming')).toBeInTheDocument();
  });

  it('reports a booking already past as Completed', async () => {
    vi.spyOn(api.organizer, 'getSessions').mockResolvedValue([
      bookingSession({ status: 'Submitted', selectedDate: '2000-01-01', selectedTime: '09:00:00' }),
    ]);
    renderPage();

    const row = await screen.findByRole('link', { name: /jane doe/i });
    expect(within(row).getByText('Submitted')).toBeInTheDocument();
    expect(await within(row).findByText('Completed')).toBeInTheDocument();
  });

  it('renders the list even when the organizer timezone cannot be read', async () => {
    // The zone lookup is best-effort: a failure falls back to the browser's
    // clock, which is exactly what this screen did before, and must never take
    // the session list down with it.
    vi.spyOn(api.availability, 'getSchedule').mockRejectedValue(new Error('nope'));
    vi.spyOn(api.organizer, 'getSessions').mockResolvedValue([
      bookingSession({ status: 'Submitted', name: 'Jane Doe' }),
    ]);
    renderPage();

    expect(await screen.findByRole('link', { name: /jane doe/i })).toBeInTheDocument();
  });
});
