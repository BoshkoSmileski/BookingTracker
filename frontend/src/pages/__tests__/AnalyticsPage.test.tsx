import { screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AnalyticsPage } from '../AnalyticsPage';
import { api } from '../../lib/api';
import {
  activityEntry, bookingAnalytics, bookingPageSummary, conversionFunnel, operationalAnalytics,
} from '../../test/factories';
import { renderWithProviders } from '../../test/render';
import { resetAuthState } from '../../test/authContextMock';

vi.mock('../../contexts/AuthContext', () => import('../../test/authContextMock'));

/**
 * REGRESSION AREA: analytics calculations reaching the screen intact - shares,
 * rates and durations are computed server-side, so the page's job is to render
 * them faithfully and to apply one filter consistently to every panel.
 */
describe('AnalyticsPage', () => {
  function mockAll(overrides: {
    bookings?: ReturnType<typeof bookingAnalytics>;
    funnel?: ReturnType<typeof conversionFunnel>;
    operations?: ReturnType<typeof operationalAnalytics>;
  } = {}) {
    vi.spyOn(api.organizer, 'getMyBookingPages').mockResolvedValue([
      bookingPageSummary(),
      bookingPageSummary({ id: '55555555-5555-5555-5555-555555555555', title: 'Discovery Call', slug: 'discovery' }),
    ]);
    const bookings = vi.spyOn(api.analytics, 'getBookings').mockResolvedValue(overrides.bookings ?? bookingAnalytics());
    const funnel = vi.spyOn(api.analytics, 'getFunnel').mockResolvedValue(overrides.funnel ?? conversionFunnel());
    const operations = vi.spyOn(api.analytics, 'getOperations').mockResolvedValue(overrides.operations ?? operationalAnalytics());
    const activity = vi.spyOn(api.analytics, 'getActivity').mockResolvedValue([
      activityEntry(),
      activityEntry({ kind: 'EmailFailed', description: 'Reminder email to jane@example.com failed' }),
    ]);
    return { bookings, funnel, operations, activity };
  }

  const renderPage = () => renderWithProviders(<AnalyticsPage />, { route: '/dashboard/analytics' });

  /**
   * Scopes a query to one panel. The same figure legitimately appears in several
   * places (20 is both the Visitors card and the doughnut total; "Booking
   * confirmed" is both a funnel step and an activity entry), so page-wide text
   * queries would be ambiguous by design rather than by mistake.
   */
  const panel = (name: RegExp) => within(screen.getByRole('heading', { name }).closest('section')!);

  /** A stat card renders its label and value as adjacent paragraphs. */
  const statValue = (label: string) => screen.getByText(label).parentElement!.querySelector('p:nth-of-type(2)')!;

  beforeEach(() => {
    resetAuthState();
  });

  it('renders headline figures once data arrives', async () => {
    mockAll();
    renderPage();

    expect(await screen.findByText('Visitors')).toBeInTheDocument();
    expect(statValue('Visitors')).toHaveTextContent('20');
    expect(statValue('Bookings')).toHaveTextContent('8');
    expect(statValue('Completion rate')).toHaveTextContent('40%');
  });

  it('shows loading skeletons before the first response', async () => {
    mockAll();
    const { container } = renderPage();

    expect(container.querySelectorAll('.animate-pulse').length).toBeGreaterThan(0);
    await screen.findByText('Visitors');
  });

  it('renders every major panel', async () => {
    mockAll();
    renderPage();

    expect(await screen.findByRole('heading', { name: /booking trend/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /status breakdown/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /conversion funnel/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /abandonment/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /popular weekdays/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /popular hours/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /email delivery/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /reminders/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /calendar sync/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /recent activity/i })).toBeInTheDocument();
  });

  it('requests all four analytics endpoints with the same filter window', async () => {
    const spies = mockAll();
    renderPage();
    await screen.findByText('Visitors');

    const filters = [
      spies.bookings.mock.calls[0][1], spies.funnel.mock.calls[0][1],
      spies.operations.mock.calls[0][1], spies.activity.mock.calls[0][1],
    ];
    // Every panel must agree on the window, or two cards on one screen disagree.
    expect(new Set(filters.map((f) => `${f.from}|${f.to}`)).size).toBe(1);
    expect(filters[0].from).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });

  it('defaults to the last 30 days', async () => {
    const spies = mockAll();
    renderPage();
    await screen.findByText('Visitors');

    const { from, to } = spies.bookings.mock.calls[0][1];
    const days = Math.round((new Date(to!).getTime() - new Date(from!).getTime()) / 86_400_000);
    expect(days).toBe(29); // inclusive range
    expect(screen.getByRole('button', { name: 'Last 30 days' })).toBeInTheDocument();
  });

  it('refetches with a new window when the range is changed', async () => {
    const spies = mockAll();
    const { user } = renderPage();
    await screen.findByText('Visitors');

    await user.click(screen.getByRole('button', { name: 'Last 7 days' }));

    await waitFor(() => expect(spies.bookings.mock.calls.length).toBeGreaterThan(1));
    const latest = spies.bookings.mock.calls.at(-1)![1];
    const days = Math.round((new Date(latest.to!).getTime() - new Date(latest.from!).getTime()) / 86_400_000);
    expect(days).toBe(6);
  });

  it('refetches scoped to one booking page when that filter is used', async () => {
    const spies = mockAll();
    const { user } = renderPage();
    await screen.findByText('Visitors');

    await user.selectOptions(screen.getByLabelText(/filter by booking page/i), '55555555-5555-5555-5555-555555555555');

    await waitFor(() => {
      expect(spies.bookings.mock.calls.at(-1)![1].bookingPageId).toBe('55555555-5555-5555-5555-555555555555');
    });
  });

  it('refetches scoped to one status when that filter is used', async () => {
    const spies = mockAll();
    const { user } = renderPage();
    await screen.findByText('Visitors');

    await user.selectOptions(screen.getByLabelText(/filter by status/i), 'Submitted');

    await waitFor(() => expect(spies.bookings.mock.calls.at(-1)![1].status).toBe('Submitted'));
  });

  it('renders the funnel steps with their counts', async () => {
    mockAll();
    renderPage();

    await screen.findByText('Visitors');
    const funnel = panel(/conversion funnel/i);
    expect(funnel.getByText('Page viewed')).toBeInTheDocument();
    expect(funnel.getByText('Booking confirmed')).toBeInTheDocument();
    expect(funnel.getByText('20')).toBeInTheDocument();
    expect(funnel.getByText(/40.0% overall/)).toBeInTheDocument();
  });

  it('summarises abandonment with rate and most common drop-off', async () => {
    mockAll();
    renderPage();

    expect(await screen.findByRole('heading', { name: /abandonment/i })).toBeInTheDocument();
    const abandonment = panel(/^abandonment$/i);
    expect(abandonment.getByText('30%')).toBeInTheDocument();
    expect(abandonment.getByText(/most common drop-off/i)).toBeInTheDocument();
  });

  it('shows the page performance table only when there is more than one page', async () => {
    mockAll();
    renderPage();
    await screen.findByText('Visitors');

    // The seeded analytics carry a single page, so a comparison table would be noise.
    expect(screen.queryByRole('columnheader', { name: 'Conversion' })).not.toBeInTheDocument();
  });

  it('renders the page performance table when multiple pages have data', async () => {
    const analytics = bookingAnalytics();
    mockAll({
      bookings: bookingAnalytics({
        pagePerformance: [
          ...analytics.pagePerformance,
          {
            bookingPageId: '55555555-5555-5555-5555-555555555555', title: 'Discovery Call', slug: 'discovery',
            isActive: false, views: 5, bookings: 1, cancelled: 0, upcoming: 1, conversionRate: 0.2, cancellationRate: 0,
          },
        ],
      }),
    });
    renderPage();

    const table = await screen.findByRole('table');
    expect(within(table).getByRole('columnheader', { name: 'Conversion' })).toBeInTheDocument();
    expect(within(table).getByText('Discovery Call')).toBeInTheDocument();
    expect(within(table).getByText('disabled')).toBeInTheDocument();
  });

  it('renders operational panels including calendar coverage', async () => {
    mockAll();
    renderPage();

    expect(await screen.findByText('Sync coverage')).toBeInTheDocument();
    const calendar = panel(/calendar sync/i);
    expect(calendar.getByText('6/8')).toBeInTheDocument();
    expect(calendar.getByText('organizer@gmail.com')).toBeInTheDocument();
  });

  it('invites the organizer to connect a calendar when none is linked', async () => {
    const ops = operationalAnalytics();
    mockAll({
      operations: {
        ...ops,
        calendar: { ...ops.calendar, connected: false, accountEmail: null, syncCoverage: null },
      },
    });
    renderPage();

    expect(await screen.findByText(/no calendar connected/i)).toBeInTheDocument();
  });

  it('renders the activity feed', async () => {
    mockAll();
    renderPage();

    await screen.findByText('Visitors');
    const activity = panel(/recent activity/i);
    expect(activity.getByText('Booking confirmed')).toBeInTheDocument();
    expect(activity.getByText(/reminder email to jane@example.com failed/i)).toBeInTheDocument();
  });

  it('surfaces a load failure instead of rendering an empty dashboard', async () => {
    vi.spyOn(api.organizer, 'getMyBookingPages').mockResolvedValue([]);
    vi.spyOn(api.analytics, 'getBookings').mockRejectedValue(new Error('boom'));
    vi.spyOn(api.analytics, 'getFunnel').mockResolvedValue(conversionFunnel());
    vi.spyOn(api.analytics, 'getOperations').mockResolvedValue(operationalAnalytics());
    vi.spyOn(api.analytics, 'getActivity').mockResolvedValue([]);
    renderPage();

    expect(await screen.findByText(/failed to load analytics/i)).toBeInTheDocument();
  });

  describe('export', () => {
    /** A resolved export plus a spy on the anchor saveFile drives, so nothing navigates under jsdom. */
    function interceptDownload() {
      const anchor = document.createElement('a');
      vi.spyOn(anchor, 'click').mockImplementation(() => {});
      vi.spyOn(document, 'createElement').mockImplementation((tag: string) =>
        (tag === 'a' ? anchor : originalCreateElement.call(document, tag)) as HTMLElement);
      return anchor;
    }

    const originalCreateElement = document.createElement;

    it('offers both formats', async () => {
      mockAll();
      renderPage();

      expect(await screen.findByRole('button', { name: 'Export CSV' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Export PDF' })).toBeInTheDocument();
    });

    it('exports CSV under the filter the dashboard is currently showing', async () => {
      const spies = mockAll();
      const csv = vi.spyOn(api.analytics, 'exportCsv')
        .mockResolvedValue({ blob: new Blob(['a,b']), fileName: 'analytics-30d.csv' });
      const anchor = interceptDownload();
      const { user } = renderPage();
      await screen.findByText('Visitors');

      await user.click(screen.getByRole('button', { name: 'Export CSV' }));

      await waitFor(() => expect(csv).toHaveBeenCalledOnce());
      // The export and the panels must agree on the window, or the file
      // describes something other than what is on screen.
      expect(csv.mock.calls[0][1]).toEqual(spies.bookings.mock.calls[0][1]);
      expect(anchor.download).toBe('analytics-30d.csv');
    });

    it('exports PDF from the same filter', async () => {
      const spies = mockAll();
      const pdf = vi.spyOn(api.analytics, 'exportPdf')
        .mockResolvedValue({ blob: new Blob(['%PDF-1.4']), fileName: 'analytics.pdf' });
      interceptDownload();
      const { user } = renderPage();
      await screen.findByText('Visitors');

      await user.click(screen.getByRole('button', { name: 'Export PDF' }));

      await waitFor(() => expect(pdf).toHaveBeenCalledOnce());
      expect(pdf.mock.calls[0][1]).toEqual(spies.bookings.mock.calls[0][1]);
    });

    it('carries a changed filter into the export', async () => {
      mockAll();
      const csv = vi.spyOn(api.analytics, 'exportCsv')
        .mockResolvedValue({ blob: new Blob(['a,b']), fileName: 'analytics.csv' });
      interceptDownload();
      const { user } = renderPage();
      await screen.findByText('Visitors');

      await user.click(screen.getByRole('button', { name: 'Last 7 days' }));
      await user.selectOptions(screen.getByLabelText(/filter by status/i), 'Submitted');
      await user.click(screen.getByRole('button', { name: 'Export CSV' }));

      await waitFor(() => expect(csv).toHaveBeenCalledOnce());
      const exported = csv.mock.calls[0][1];
      expect(exported.status).toBe('Submitted');
      const days = Math.round((new Date(exported.to!).getTime() - new Date(exported.from!).getTime()) / 86_400_000);
      expect(days).toBe(6);
    });

    it('shows progress and disables both buttons while an export is running', async () => {
      mockAll();
      let release: (value: { blob: Blob; fileName: string }) => void = () => {};
      vi.spyOn(api.analytics, 'exportPdf').mockReturnValue(new Promise((resolve) => { release = resolve; }));
      interceptDownload();
      const { user } = renderPage();
      await screen.findByText('Visitors');

      await user.click(screen.getByRole('button', { name: 'Export PDF' }));

      // A second request would only produce a second copy of the same file.
      expect(await screen.findByRole('button', { name: /preparing/i })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Export CSV' })).toBeDisabled();

      release({ blob: new Blob(['%PDF-1.4']), fileName: 'analytics.pdf' });

      await waitFor(() => expect(screen.getByRole('button', { name: 'Export PDF' })).toBeEnabled());
    });

    it('reports a failed export without blanking the dashboard', async () => {
      mockAll();
      const { ApiError } = await import('../../lib/api');
      vi.spyOn(api.analytics, 'exportCsv').mockRejectedValue(new ApiError(500, { title: 'Report generation failed.' }));
      interceptDownload();
      const { user } = renderPage();
      await screen.findByText('Visitors');

      await user.click(screen.getByRole('button', { name: 'Export CSV' }));

      expect(await screen.findByRole('alert')).toHaveTextContent('Report generation failed.');
      // Figures that loaded perfectly well must still be on screen.
      expect(statValue('Visitors')).toHaveTextContent('20');
      expect(screen.getByRole('button', { name: 'Export CSV' })).toBeEnabled();
    });

    it('reports an unreachable server rather than borrowing the export’s wording', async () => {
      mockAll();
      const { NetworkError } = await import('../../lib/api');
      vi.spyOn(api.analytics, 'exportPdf').mockRejectedValue(new NetworkError());
      interceptDownload();
      const { user } = renderPage();
      await screen.findByText('Visitors');

      await user.click(screen.getByRole('button', { name: 'Export PDF' }));

      expect(await screen.findByRole('alert')).toHaveTextContent(/could not reach the server/i);
    });
  });

  it('reports an unreachable server differently from a rejected request', async () => {
    // REGRESSION: a connectivity failure must not borrow the caller's domain wording.
    const { NetworkError } = await import('../../lib/api');
    vi.spyOn(api.organizer, 'getMyBookingPages').mockResolvedValue([]);
    vi.spyOn(api.analytics, 'getBookings').mockRejectedValue(new NetworkError());
    vi.spyOn(api.analytics, 'getFunnel').mockResolvedValue(conversionFunnel());
    vi.spyOn(api.analytics, 'getOperations').mockResolvedValue(operationalAnalytics());
    vi.spyOn(api.analytics, 'getActivity').mockResolvedValue([]);
    renderPage();

    expect(await screen.findByText(/could not reach the server/i)).toBeInTheDocument();
  });
});

/**
 * This screen always caught its failures - what it did with them was the
 * problem. Every panel falls back to its own empty state, so a failed load
 * rendered "No activity in this range yet", "No abandoned sessions in this
 * range", "No calendar connected": eight confident statements about data the
 * app never received, under one error notice.
 */
describe('AnalyticsPage — the dashboard could not be loaded', () => {
  beforeEach(() => {
    resetAuthState();
    vi.spyOn(api.organizer, 'getMyBookingPages').mockResolvedValue([]);
  });

  const rejectAll = () => {
    vi.spyOn(api.analytics, 'getBookings').mockRejectedValue(new Error('boom'));
    vi.spyOn(api.analytics, 'getFunnel').mockRejectedValue(new Error('boom'));
    vi.spyOn(api.analytics, 'getOperations').mockRejectedValue(new Error('boom'));
    vi.spyOn(api.analytics, 'getActivity').mockRejectedValue(new Error('boom'));
  };

  it('replaces the panels rather than letting them answer for themselves', async () => {
    rejectAll();
    renderWithProviders(<AnalyticsPage />, { route: '/dashboard/analytics' });

    await screen.findByRole('alert');
    expect(screen.queryByText('No activity in this range yet.')).not.toBeInTheDocument();
    expect(screen.queryByText(/no calendar connected/i)).not.toBeInTheDocument();
    expect(screen.queryByText('Recent activity')).not.toBeInTheDocument();
  });

  it('keeps the filters usable, so the range can be changed and retried', async () => {
    rejectAll();
    renderWithProviders(<AnalyticsPage />, { route: '/dashboard/analytics' });

    await screen.findByRole('alert');
    expect(screen.getByRole('button', { name: 'Last 7 days' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
  });

  it('retries and renders the figures when the retry succeeds', async () => {
    const getBookings = vi.spyOn(api.analytics, 'getBookings')
      .mockRejectedValueOnce(new Error('boom'))
      .mockResolvedValue(bookingAnalytics());
    vi.spyOn(api.analytics, 'getFunnel').mockResolvedValue(conversionFunnel());
    vi.spyOn(api.analytics, 'getOperations').mockResolvedValue(operationalAnalytics());
    vi.spyOn(api.analytics, 'getActivity').mockResolvedValue([activityEntry()]);

    const { user } = renderWithProviders(<AnalyticsPage />, { route: '/dashboard/analytics' });
    await screen.findByRole('alert');
    await user.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByText('Booking trend')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(getBookings).toHaveBeenCalledTimes(2);
  });

  it('keeps figures already on screen when a later filter change fails', async () => {
    // The refresh case: those numbers are still true of the range they were
    // fetched for, so blanking them would be a worse answer than a stale one.
    vi.spyOn(api.analytics, 'getBookings')
      .mockResolvedValueOnce(bookingAnalytics())
      .mockRejectedValue(new Error('boom'));
    vi.spyOn(api.analytics, 'getFunnel').mockResolvedValue(conversionFunnel());
    vi.spyOn(api.analytics, 'getOperations').mockResolvedValue(operationalAnalytics());
    vi.spyOn(api.analytics, 'getActivity').mockResolvedValue([activityEntry()]);

    const { user } = renderWithProviders(<AnalyticsPage />, { route: '/dashboard/analytics' });
    await screen.findByText('Booking trend');

    await user.click(screen.getByRole('button', { name: 'Last 7 days' }));

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.getByText('Booking trend')).toBeInTheDocument();
  });
});
