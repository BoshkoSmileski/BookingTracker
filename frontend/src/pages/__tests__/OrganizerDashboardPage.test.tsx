import { screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { OrganizerDashboardPage } from '../OrganizerDashboardPage';
import { api } from '../../lib/api';
import { bookingSession, workingSchedule } from '../../test/factories';
import { formatCalendarDateTime } from '../../lib/calendarDates';
import { renderWithProviders } from '../../test/render';
import { resetAuthState } from '../../test/authContextMock';
import type { BookingSessionStatus } from '../../lib/types';

vi.mock('../../contexts/AuthContext', () => import('../../test/authContextMock'));
// The live hub is out of scope here; a no-op keeps it from opening a socket.
vi.mock('../../lib/signalr', () => ({
  createDashboardConnection: () => ({
    on: () => {},
    start: () => Promise.resolve(),
    invoke: () => Promise.resolve(),
    stop: () => Promise.resolve(),
  }),
}));

describe('OrganizerDashboardPage', () => {
  const PAGE_ID = '11111111-1111-1111-1111-111111111111';

  const renderAt = (route: string | { pathname: string; state?: unknown }) =>
    renderWithProviders(<OrganizerDashboardPage />, { route, path: '/dashboard/:pageId' });

  /** Exactly what the create form navigates with. */
  const justCreated = { pathname: `/dashboard/${PAGE_ID}`, state: { justCreatedPage: true, createdSlug: 'discovery-call' } };

  beforeEach(() => {
    resetAuthState();
    // DashboardSummary loads alongside the list; it is covered elsewhere.
    vi.spyOn(api.availability, 'getExceptions').mockResolvedValue([]);
    vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(null);
  });

  /** Returns the spy so a test can assert which status the list actually requested. */
  function mockSessions(sessions = [bookingSession()]) {
    return vi.spyOn(api.organizer, 'getSessions').mockResolvedValue(sessions);
  }

  it('defaults to Active when the URL says nothing', async () => {
    const getSessions = mockSessions([]);
    renderAt(`/dashboard/${PAGE_ID}`);

    await waitFor(() => expect(getSessions).toHaveBeenCalled());
    expect(getSessions.mock.calls.some((c) => c[2] === 'Active')).toBe(true);
    expect(screen.getByRole('button', { name: 'Active', pressed: true })).toBeInTheDocument();
  });

  it.each<BookingSessionStatus>(['Submitted', 'Cancelled', 'Abandoned'])(
    'restores the %s filter straight from the URL',
    async (status) => {
      // REGRESSION: the filter lived in component state, so returning from a
      // session remounted the page and silently reset the tab to Active.
      const getSessions = mockSessions([]);
      renderAt(`/dashboard/${PAGE_ID}?status=${status}`);

      await waitFor(() => expect(getSessions).toHaveBeenCalled());
      expect(getSessions.mock.calls.some((c) => c[2] === status)).toBe(true);
      expect(screen.getByRole('button', { name: status, pressed: true })).toBeInTheDocument();
    },
  );

  it('carries the current filter into each session link, so Back returns to it', async () => {
    mockSessions([bookingSession({ id: 'session-9', status: 'Submitted' })]);
    renderAt(`/dashboard/${PAGE_ID}?status=Submitted`);

    const link = await screen.findByRole('link', { name: /jane doe/i });
    expect(link).toHaveAttribute('href', `/dashboard/${PAGE_ID}/sessions/session-9?status=Submitted`);
  });

  it('puts a chosen tab in the URL, so the list can be linked and reloaded', async () => {
    mockSessions([]);
    const { user } = renderAt(`/dashboard/${PAGE_ID}`);
    await screen.findByRole('button', { name: 'Cancelled' });

    await user.click(screen.getByRole('button', { name: 'Cancelled' }));

    // The filters are a toggle-button group, not a tablist: there is no tabpanel
    // and no arrow-key movement between them, so 'pressed' is the honest state.
    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Cancelled', pressed: true })).toBeInTheDocument(),
    );
    const filters = within(screen.getByRole('group', { name: /filter sessions by status/i })).getAllByRole('button');
    expect(filters).toHaveLength(4);
  });

  it('states both the status and whether it has happened, in one badge', async () => {
    // Two adjacent pills - a grey "Upcoming" beside a green "Submitted" - read
    // as competing labels for the same fact.
    mockSessions([bookingSession({ status: 'Submitted', selectedDate: '2099-01-01', selectedTime: '09:00:00' })]);
    renderAt(`/dashboard/${PAGE_ID}?status=Submitted`);

    // Scoped to the row: "Upcoming" is also a panel heading in the summary above.
    const row = await screen.findByRole('link', { name: /jane doe/i });
    expect(within(row).getByText('Submitted')).toBeInTheDocument();
    expect(within(row).getByText('Upcoming')).toBeInTheDocument();
  });

  it('formats the appointment rather than printing the raw ISO values', async () => {
    mockSessions([bookingSession({ selectedDate: '2026-08-20', selectedTime: '14:30:00' })]);
    renderAt(`/dashboard/${PAGE_ID}?status=Submitted`);

    // Derived from the same formatter the page uses - a hardcoded string here
    // would only pass in one locale.
    expect(await screen.findByText(formatCalendarDateTime('2026-08-20', '14:30:00'))).toBeInTheDocument();
    expect(screen.queryByText(/2026-08-20 at/)).not.toBeInTheDocument();
  });

  it('does not name a screen that no longer exists', async () => {
    // "Blocked dates" and "Date overrides" were consolidated into Date
    // exceptions; this strip lists only the subtractive half, so it takes the
    // word that screen puts on those rows rather than the merged screen's name,
    // which it would not be telling the truth about.
    vi.spyOn(api.availability, 'getExceptions').mockResolvedValue([]);
    mockSessions([]);
    renderAt(`/dashboard/${PAGE_ID}`);

    expect(await screen.findByRole('heading', { name: 'Unavailable dates' })).toBeInTheDocument();
    expect(screen.queryByText('Blocked dates')).not.toBeInTheDocument();
    expect(screen.queryByText('Date overrides')).not.toBeInTheDocument();
  });

  it('explains an empty tab instead of printing a bare sentence', async () => {
    mockSessions([]);
    renderAt(`/dashboard/${PAGE_ID}?status=Abandoned`);

    expect(await screen.findByText('No abandoned sessions')).toBeInTheDocument();
  });

  /**
   * Creating a booking page used to land here on "No active sessions" - correct
   * for a page nobody has visited, and a dead end: the public link, the one
   * thing needed at that moment, was not on the screen at all.
   */
  describe('straight after the page was created', () => {
    it('offers the public link, with a way to copy and to preview it', async () => {
      vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(workingSchedule());
      mockSessions([]);
      renderAt(justCreated);

      const panel = await screen.findByRole('region', { name: /booking page created/i });
      expect(within(panel).getByText(/your booking page is live/i)).toBeInTheDocument();
      expect(within(panel).getByText(new RegExp('/book/discovery-call'))).toBeInTheDocument();
      expect(within(panel).getByRole('button', { name: /copy link/i })).toBeInTheDocument();
      expect(within(panel).getByRole('link', { name: /preview/i })).toHaveAttribute(
        'href',
        '/book/discovery-call',
      );
    });

    it('copies the link to the clipboard and confirms it', async () => {
      vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(workingSchedule());
      mockSessions([]);
      const { user } = renderAt(justCreated);

      const copy = await screen.findByRole('button', { name: /copy link/i });
      const writeText = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue(undefined);
      await user.click(copy);

      expect(writeText).toHaveBeenCalledWith(expect.stringContaining('/book/discovery-call'));
      expect(await screen.findByRole('button', { name: /copied/i })).toBeInTheDocument();
    });

    it('points at the settings a new page most often wants', async () => {
      vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(workingSchedule());
      mockSessions([]);
      renderAt(justCreated);

      const panel = await screen.findByRole('region', { name: /booking page created/i });
      expect(within(panel).getByRole('link', { name: 'Working hours' })).toHaveAttribute(
        'href',
        `/dashboard/${PAGE_ID}/settings/hours`,
      );
      expect(within(panel).getByRole('link', { name: 'Meeting type' })).toBeInTheDocument();
      expect(within(panel).getByRole('link', { name: 'Booking instructions' })).toBeInTheDocument();
    });

    it('sends the organizer to Working hours when there is nothing bookable', async () => {
      // The one case a link is not the useful next action: an organizer who has
      // switched every day off keeps that schedule, so the page really cannot
      // be booked yet.
      vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(
        workingSchedule({ days: [{ dayOfWeek: 1, isEnabled: false, intervals: [] }] }),
      );
      mockSessions([]);
      renderAt(justCreated);

      const panel = await screen.findByRole('region', { name: /booking page created/i });
      expect(within(panel).getByText(/no working hours to book yet/i)).toBeInTheDocument();
      expect(within(panel).getByRole('link', { name: /set working hours/i })).toHaveAttribute(
        'href',
        `/dashboard/${PAGE_ID}/settings/hours`,
      );
      expect(within(panel).queryByRole('button', { name: /copy link/i })).not.toBeInTheDocument();
    });

    it('is absent on a page reached any other way', async () => {
      vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(workingSchedule());
      mockSessions([]);
      renderAt(`/dashboard/${PAGE_ID}`);

      await screen.findByText('No active sessions');
      expect(screen.queryByRole('region', { name: /booking page created/i })).not.toBeInTheDocument();
    });
  });
});

/**
 * The list's failure mode was the clearest example of the bug this pass is
 * about: with no `catch` the skeleton stayed for ever, and had the request
 * resolved empty the screen would have read "No submitted sessions" - a
 * statement about this organizer's bookings, from a request that never
 * returned.
 */
describe('OrganizerDashboardPage — the sessions could not be loaded', () => {
  const PAGE_ID = '11111111-1111-1111-1111-111111111111';

  beforeEach(() => {
    resetAuthState();
    // The summary band is its own request; stub it so only the list is at issue.
    vi.spyOn(api.availability, 'getExceptions').mockResolvedValue([]);
    vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(workingSchedule());
  });

  const renderPage = () =>
    renderWithProviders(<OrganizerDashboardPage />, {
      route: `/dashboard/${PAGE_ID}`,
      path: '/dashboard/:pageId',
    });

  it('reports the failure instead of an empty session list', async () => {
    vi.spyOn(api.organizer, 'getSessions').mockRejectedValue(new Error('boom'));
    renderPage();

    // The summary band above reads sessions too, so both report - which is the
    // intended shape: each says only for itself, rather than one taking the
    // screen on the other's behalf.
    expect((await screen.findAllByRole('alert')).length).toBeGreaterThan(0);
    expect(screen.queryByText('No active sessions')).not.toBeInTheDocument();
  });

  it('retries and renders the sessions when the retry succeeds', async () => {
    const getSessions = vi.spyOn(api.organizer, 'getSessions')
      .mockRejectedValueOnce(new Error('boom'))
      .mockResolvedValue([bookingSession({ name: 'Ada Lovelace' })]);
    const { user } = renderPage();

    await screen.findAllByRole('alert');
    // The list's own retry, not the summary's - they are separate requests.
    const listPanel = screen.getByRole('group', { name: /filter sessions/i }).parentElement!;
    await user.click(within(listPanel).getAllByRole('button', { name: 'Try again' }).at(-1)!);

    // As a link: the visitor's name also appears in the summary's "Next:" line.
    expect(await screen.findByRole('link', { name: /Ada Lovelace/ })).toBeInTheDocument();
    expect(getSessions.mock.calls.length).toBeGreaterThan(1);
  });
});
