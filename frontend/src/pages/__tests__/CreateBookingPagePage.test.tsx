import { screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { CreateBookingPagePage } from '../CreateBookingPagePage';
import { api } from '../../lib/api';
import { bookingPageDetail, workingSchedule } from '../../test/factories';
import { renderWithProviders } from '../../test/render';
import { resetAuthState } from '../../test/authContextMock';

vi.mock('../../contexts/AuthContext', () => import('../../test/authContextMock'));

/**
 * The first screen a new organizer ever completes.
 *
 * Two things are pinned. The form used to fork on availability and send an
 * organizer with no working hours to a settings screen instead of their new
 * page - a consequence of there being no hours to send them to, which creating
 * the page now handles server-side. And the optional booking limits, three
 * questions a first-time organizer has no basis to answer, sat between them and
 * the button.
 */
describe('CreateBookingPagePage', () => {
  const PAGE_ID = '22222222-2222-2222-2222-222222222222';

  const renderPage = () => renderWithProviders(<CreateBookingPagePage />, { route: '/dashboard/new' });

  async function fillAndSubmit(user: ReturnType<typeof renderWithProviders>['user'], title = 'Discovery call') {
    await user.type(await screen.findByLabelText('Title'), title);
    await user.click(screen.getByRole('button', { name: /create booking page/i }));
  }

  beforeEach(() => {
    resetAuthState();
    vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(workingSchedule());
    vi.spyOn(api.organizer, 'createBookingPage').mockResolvedValue(
      bookingPageDetail({ id: PAGE_ID, slug: 'discovery-call' }),
    );
  });

  it('sends the browser’s time zone, so a first schedule lands on the right clock', async () => {
    const create = vi
      .spyOn(api.organizer, 'createBookingPage')
      .mockResolvedValue(bookingPageDetail({ id: PAGE_ID }));
    const { user } = renderPage();

    await fillAndSubmit(user);

    await waitFor(() =>
      expect(create).toHaveBeenCalledWith(
        expect.any(String),
        expect.objectContaining({ timeZoneId: Intl.DateTimeFormat().resolvedOptions().timeZone }),
      ),
    );
  });

  it('states the hours a first schedule will be created with, rather than warning about their absence', async () => {
    // Creating the page creates the schedule, so the honest thing to say is
    // what those hours are.
    vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(null);
    renderPage();

    const notice = await screen.findByText(/monday.*friday, 09:00.*17:00/i);
    expect(notice).toHaveTextContent(Intl.DateTimeFormat().resolvedOptions().timeZone);
    expect(screen.queryByText(/haven’t set your working hours/i)).not.toBeInTheDocument();
  });

  it('says an existing schedule will be used', async () => {
    renderPage();

    expect(await screen.findByText(/uses your default working schedule/i)).toBeInTheDocument();
  });

  it('keeps the optional booking limits out of the way until asked for', async () => {
    const { user } = renderPage();

    const limits = await screen.findByText(/booking limits/i);
    expect(screen.queryByLabelText(/minimum notice/i)).not.toBeVisible();

    await user.click(limits);

    expect(screen.getByLabelText(/minimum notice/i)).toBeVisible();
  });

  it('still submits the limits when they are filled in', async () => {
    const create = vi
      .spyOn(api.organizer, 'createBookingPage')
      .mockResolvedValue(bookingPageDetail({ id: PAGE_ID }));
    const { user } = renderPage();

    await user.click(await screen.findByText(/booking limits/i));
    await user.type(screen.getByLabelText(/max per day/i), '4');
    await fillAndSubmit(user);

    await waitFor(() =>
      expect(create).toHaveBeenCalledWith(expect.any(String), expect.objectContaining({ maxBookingsPerDay: 4 })),
    );
  });

  it('reports a failed creation instead of navigating', async () => {
    vi.spyOn(api.organizer, 'createBookingPage').mockRejectedValue(new Error('boom'));
    const { user } = renderPage();

    await fillAndSubmit(user);

    expect(await screen.findByRole('alert')).toHaveTextContent(/failed to create/i);
  });
});
