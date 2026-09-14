import { screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { DashboardHomePage } from '../DashboardHomePage';
import { ApiError, api } from '../../lib/api';
import { bookingPageSummary } from '../../test/factories';
import { renderWithProviders } from '../../test/render';
import { resetAuthState } from '../../test/authContextMock';

vi.mock('../../contexts/AuthContext', () => import('../../test/authContextMock'));

describe('DashboardHomePage', () => {
  const renderPage = () => renderWithProviders(<DashboardHomePage />, { route: '/dashboard' });

  beforeEach(() => {
    resetAuthState();
    vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(null);
  });

  it('names what the screen lists', async () => {
    // The organizer's name and the Sign out control moved into DashboardLayout,
    // where they are on every screen rather than only this one - see its tests.
    vi.spyOn(api.organizer, 'getMyBookingPages').mockResolvedValue([bookingPageSummary()]);
    renderPage();

    expect(await screen.findByRole('heading', { name: 'Booking pages' })).toBeInTheDocument();
  });

  it('lists every booking page rather than auto-redirecting when there is exactly one', async () => {
    // REGRESSION: a single page used to skip the list entirely, hiding the
    // "create another" path.
    vi.spyOn(api.organizer, 'getMyBookingPages').mockResolvedValue([bookingPageSummary()]);
    renderPage();

    expect(await screen.findByText('30 Minute Meeting')).toBeInTheDocument();
  });

  it('lists multiple booking pages with their active state', async () => {
    vi.spyOn(api.organizer, 'getMyBookingPages').mockResolvedValue([
      bookingPageSummary(),
      bookingPageSummary({ id: '2', slug: 'discovery', title: 'Discovery Call', isActive: false }),
    ]);
    renderPage();

    expect(await screen.findByText('30 Minute Meeting')).toBeInTheDocument();
    expect(screen.getByText('Discovery Call')).toBeInTheDocument();
    expect(screen.getByText('Active')).toBeInTheDocument();
    expect(screen.getByText('Disabled')).toBeInTheDocument();
  });

  it('links each page to its dashboard', async () => {
    vi.spyOn(api.organizer, 'getMyBookingPages').mockResolvedValue([bookingPageSummary()]);
    renderPage();

    const link = await screen.findByRole('link', { name: '30 Minute Meeting' });
    expect(link).toHaveAttribute('href', '/dashboard/11111111-1111-1111-1111-111111111111');
  });

  it('offers the create path from the list itself', async () => {
    // Analytics is reached from the shell's workspace nav now, not from here -
    // DashboardLayout's tests cover that; this page owns the list-level actions.
    vi.spyOn(api.organizer, 'getMyBookingPages').mockResolvedValue([bookingPageSummary()]);
    renderPage();

    expect(await screen.findByRole('link', { name: 'New booking page' })).toHaveAttribute('href', '/dashboard/new');
  });

  it('toggles a page between enabled and disabled', async () => {
    vi.spyOn(api.organizer, 'getMyBookingPages').mockResolvedValue([bookingPageSummary({ isActive: true })]);
    const setActive = vi.spyOn(api.organizer, 'setBookingPageActive')
      .mockResolvedValue({} as never);
    const { user } = renderPage();
    await screen.findByText('30 Minute Meeting');

    await user.click(screen.getByRole('button', { name: 'Disable' }));

    await waitFor(() => expect(setActive).toHaveBeenCalled());
    expect(setActive.mock.calls[0][2]).toBe(false);
  });

  it('explains a 409 when deleting a page that still has bookings', async () => {
    vi.spyOn(api.organizer, 'getMyBookingPages').mockResolvedValue([bookingPageSummary()]);
    vi.spyOn(api.organizer, 'deleteBookingPage')
      .mockRejectedValue(new ApiError(409, { title: 'This page has confirmed bookings.' }));
    const { user } = renderPage();

    await user.click(await screen.findByRole('button', { name: /^delete$/i }));
    await user.click(within(await screen.findByRole('group', { name: /delete/i }))
      .getByRole('button', { name: /delete page/i }));

    expect(await screen.findByText(/this page has confirmed bookings/i)).toBeInTheDocument();
  });

  it('asks before deleting, in the page rather than a browser dialog, and names the page', async () => {
    // Was `window.confirm`. The cards are a grid of near-identical objects, so
    // the question has to stay attached to the one being acted on.
    vi.spyOn(api.organizer, 'getMyBookingPages').mockResolvedValue([bookingPageSummary({ title: 'Discovery call' })]);
    const del = vi.spyOn(api.organizer, 'deleteBookingPage');
    const { user } = renderPage();

    await user.click(await screen.findByRole('button', { name: /^delete$/i }));

    const panel = await screen.findByRole('group', { name: /delete "discovery call"/i });
    expect(within(panel).getByText(/public link stops working/i)).toBeInTheDocument();
    expect(del).not.toHaveBeenCalled();
  });

  it('backs out of the delete confirmation without deleting', async () => {
    vi.spyOn(api.organizer, 'getMyBookingPages').mockResolvedValue([bookingPageSummary()]);
    const del = vi.spyOn(api.organizer, 'deleteBookingPage');
    const { user } = renderPage();

    await user.click(await screen.findByRole('button', { name: /^delete$/i }));
    await user.click(await screen.findByRole('button', { name: /keep page/i }));

    expect(del).not.toHaveBeenCalled();
    expect(screen.queryByRole('group', { name: /delete/i })).not.toBeInTheDocument();
  });

  it('prompts an empty organizer to create their first booking page', async () => {
    vi.spyOn(api.organizer, 'getMyBookingPages').mockResolvedValue([]);
    renderPage();

    await waitFor(() => expect(screen.getByRole('link', { name: /new booking page|create/i })).toBeInTheDocument());
  });

  it('warns when no working hours are configured, since nothing is bookable then', async () => {
    vi.spyOn(api.organizer, 'getMyBookingPages').mockResolvedValue([bookingPageSummary()]);
    vi.spyOn(api.availability, 'getSchedule').mockResolvedValue({
      id: 's', organizerId: 'o', timeZoneId: 'UTC',
      days: [{ dayOfWeek: 1, isEnabled: true, intervals: [] }],
    });
    renderPage();

    expect(await screen.findByRole('link', { name: /set working hours/i })).toBeInTheDocument();
  });

  it('offers per-page actions: edit and public preview', async () => {
    // The settings chip row (Notifications, Calendar, ...) lives on the per-page
    // dashboard via DashboardSummary - this page offers the list-level actions.
    vi.spyOn(api.organizer, 'getMyBookingPages').mockResolvedValue([bookingPageSummary()]);
    vi.spyOn(api.availability, 'getSchedule').mockResolvedValue({
      id: 's', organizerId: 'o', timeZoneId: 'UTC',
      days: [{ dayOfWeek: 1, isEnabled: true, intervals: [{ start: '09:00:00', end: '17:00:00' }] }],
    });
    renderPage();
    await screen.findByText('30 Minute Meeting');

    expect(screen.getByRole('link', { name: 'Edit' }))
      .toHaveAttribute('href', '/dashboard/11111111-1111-1111-1111-111111111111/settings/details');
    expect(screen.getByRole('link', { name: 'Preview' })).toHaveAttribute('href', expect.stringContaining('demo-30-min-meeting'));
  });

  it('does not warn about working hours once availability is configured', async () => {
    vi.spyOn(api.organizer, 'getMyBookingPages').mockResolvedValue([bookingPageSummary()]);
    vi.spyOn(api.availability, 'getSchedule').mockResolvedValue({
      id: 's', organizerId: 'o', timeZoneId: 'UTC',
      days: [{ dayOfWeek: 1, isEnabled: true, intervals: [{ start: '09:00:00', end: '17:00:00' }] }],
    });
    renderPage();
    await screen.findByText('30 Minute Meeting');

    expect(screen.queryByRole('link', { name: /set working hours/i })).not.toBeInTheDocument();
  });

  it('renders the page list as a real list for assistive tech', async () => {
    vi.spyOn(api.organizer, 'getMyBookingPages').mockResolvedValue([
      bookingPageSummary(), bookingPageSummary({ id: '2', slug: 'b', title: 'Second' }),
    ]);
    renderPage();
    await screen.findByText('30 Minute Meeting');

    const lists = screen.getAllByRole('list');
    const pageList = lists.find((l) => within(l).queryByText('30 Minute Meeting'));
    expect(pageList).toBeDefined();
    expect(within(pageList!).getAllByRole('listitem')).toHaveLength(2);
  });
});

/**
 * `pages` staying null on failure is what the skeleton keys off, so this screen
 * loaded for ever. Setting it to `[]` instead would have been worse than the
 * bug: an empty list here redirects to the create form, so a transient failure
 * would have sent an organizer with several booking pages to "Create a booking
 * page".
 */
describe('DashboardHomePage — the booking pages could not be loaded', () => {
  const renderPage = () => renderWithProviders(<DashboardHomePage />, { route: '/dashboard' });

  beforeEach(() => {
    resetAuthState();
    vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(null);
  });

  it('leaves the skeleton and reports the failure', async () => {
    vi.spyOn(api.organizer, 'getMyBookingPages').mockRejectedValue(new Error('boom'));
    renderPage();

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.queryByRole('status', { name: 'Loading' })).not.toBeInTheDocument();
  });

  it('still offers New booking page, so the screen is not a dead end', async () => {
    vi.spyOn(api.organizer, 'getMyBookingPages').mockRejectedValue(new Error('boom'));
    renderPage();

    await screen.findByRole('alert');
    expect(screen.getByRole('link', { name: /new booking page/i })).toBeInTheDocument();
  });

  it('retries and renders the pages when the retry succeeds', async () => {
    const get = vi.spyOn(api.organizer, 'getMyBookingPages')
      .mockRejectedValueOnce(new Error('boom'))
      .mockResolvedValue([bookingPageSummary({ title: 'Discovery call' })]);
    const { user } = renderPage();

    await screen.findByRole('alert');
    await user.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByRole('link', { name: 'Discovery call' })).toBeInTheDocument();
    expect(get).toHaveBeenCalledTimes(2);
  });

  it('reports a refused enable/disable rather than looking like a dead button', async () => {
    // Had no error handling at all - on the control that decides whether the
    // page is publicly bookable.
    vi.spyOn(api.organizer, 'getMyBookingPages').mockResolvedValue([
      bookingPageSummary({ title: 'Discovery call', isActive: true }),
    ]);
    vi.spyOn(api.organizer, 'setBookingPageActive').mockRejectedValue(new Error('boom'));
    const { user } = renderPage();

    await user.click(await screen.findByRole('button', { name: 'Disable' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not disable/i);
  });
});
