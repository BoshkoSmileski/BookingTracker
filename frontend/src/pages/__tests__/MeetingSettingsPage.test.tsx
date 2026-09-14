import { screen, waitForElementToBeRemoved } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MeetingSettingsPage } from '../MeetingSettingsPage';
import { NetworkError, api } from '../../lib/api';
import { bookingPageDetail, calendarConnection } from '../../test/factories';
import { renderWithProviders } from '../../test/render';
import { resetAuthState } from '../../test/authContextMock';

vi.mock('../../contexts/AuthContext', () => import('../../test/authContextMock'));

/**
 * The organizer's meeting-type selector. Driven the way an organizer drives it -
 * picking a radio and pressing Save - and asserted on what is rendered, never on
 * component state.
 */
describe('MeetingSettingsPage', () => {
  const PAGE_ID = '11111111-1111-1111-1111-111111111111';

  const renderPage = () =>
    renderWithProviders(<MeetingSettingsPage />, {
      route: `/dashboard/${PAGE_ID}/settings/meeting`,
      path: '/dashboard/:pageId/settings/meeting',
    });

  beforeEach(() => {
    resetAuthState();
    // A healthy connection is the ordinary case; the tests about the calendar
    // say which one they mean.
    vi.spyOn(api.calendar, 'getConnection').mockResolvedValue(calendarConnection());
  });

  async function renderLoaded(page = bookingPageDetail()) {
    vi.spyOn(api.organizer, 'getBookingPage').mockResolvedValue(page);
    const result = renderPage();
    await waitForElementToBeRemoved(() => screen.queryByLabelText('Loading'));
    return result;
  }

  /** The Google Meet page as it loads, so a readiness test states only the calendar. */
  const meetPage = () => bookingPageDetail({ meetingProvider: 'GoogleMeet' });

  it('shows a loading state before the page arrives', async () => {
    vi.spyOn(api.organizer, 'getBookingPage').mockResolvedValue(bookingPageDetail());
    renderPage();

    expect(screen.getByLabelText('Loading')).toBeInTheDocument();
    await waitForElementToBeRemoved(() => screen.queryByLabelText('Loading'));
  });

  it('preselects the saved meeting type', async () => {
    await renderLoaded(bookingPageDetail({ meetingProvider: 'GoogleMeet' }));

    expect(screen.getByRole('radio', { name: /google meet/i })).toBeChecked();
    expect(screen.getByRole('radio', { name: /in person/i })).not.toBeChecked();
  });

  it('defaults to in person for a page that has never been configured', async () => {
    await renderLoaded(bookingPageDetail({ meetingProvider: 'None' }));

    expect(screen.getByRole('radio', { name: /in person/i })).toBeChecked();
  });

  it('saves the selected provider and confirms it', async () => {
    const update = vi
      .spyOn(api.organizer, 'updateBookingPageMeetingSettings')
      .mockResolvedValue(bookingPageDetail({ meetingProvider: 'GoogleMeet' }));
    const { user } = await renderLoaded();

    await user.click(screen.getByRole('radio', { name: /google meet/i }));
    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(update).toHaveBeenCalledWith(expect.any(String), PAGE_ID, 'GoogleMeet');
    expect(await screen.findByText(/saved/i)).toBeInTheDocument();
  });

  it('says nothing about the calendar while the page is in person', async () => {
    // In person needs no calendar at all, so a standing caveat about one would
    // be noise on the only screen that decides it.
    vi.spyOn(api.calendar, 'getConnection').mockResolvedValue(null);
    await renderLoaded(bookingPageDetail({ meetingProvider: 'None' }));

    expect(screen.queryByText(/google meet link/i)).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /calendar/i })).not.toBeInTheDocument();
  });

  /**
   * The screen previously showed one fixed sentence whether or not a calendar
   * was connected, so the two states an organizer most needs to tell apart -
   * links will be created, links will silently not be - looked identical.
   */
  describe('what a Google Meet page will actually do', () => {
    it('warns when no calendar is connected, and offers the way to connect one', async () => {
      vi.spyOn(api.calendar, 'getConnection').mockResolvedValue(null);
      const { user } = await renderLoaded(bookingPageDetail({ meetingProvider: 'None' }));

      expect(screen.queryByText(/google calendar is not connected/i)).not.toBeInTheDocument();

      await user.click(screen.getByRole('radio', { name: /google meet/i }));

      expect(screen.getByText(/google calendar is not connected/i)).toBeInTheDocument();
      expect(screen.getByRole('link', { name: /connect google calendar/i })).toHaveAttribute(
        'href',
        `/dashboard/${PAGE_ID}/settings/calendar`,
      );
    });

    it('states that bookings still succeed without one', async () => {
      // A missing link is not a failed booking, and an organizer reading a
      // warning has no way to know that.
      vi.spyOn(api.calendar, 'getConnection').mockResolvedValue(null);
      await renderLoaded(meetPage());

      expect(screen.getByText(/still be taken, confirmed and emailed/i)).toBeInTheDocument();
    });

    it('confirms that links will be created when the calendar is healthy, naming it', async () => {
      vi.spyOn(api.calendar, 'getConnection').mockResolvedValue(
        calendarConnection({ externalCalendarName: 'Work', externalAccountEmail: 'me@gmail.com' }),
      );
      await renderLoaded(meetPage());

      const notice = screen.getByText(/new bookings on this page get a google meet link automatically/i);
      expect(notice).toHaveTextContent('Work');
      expect(notice).toHaveTextContent('me@gmail.com');
    });

    it('flags an unhealthy connection rather than calling it ready', async () => {
      vi.spyOn(api.calendar, 'getConnection').mockResolvedValue(
        calendarConnection({ status: 'ReauthorizationRequired', healthStatus: 'Needs Reauthentication' }),
      );
      await renderLoaded(meetPage());

      expect(screen.getByText(/needs attention \(Needs Reauthentication\)/i)).toBeInTheDocument();
      expect(screen.getByRole('link', { name: /fix the connection/i })).toBeInTheDocument();
    });

    it('flags a connected calendar that is not exporting bookings', async () => {
      // The subtlest of the three: everything looks connected, and no event is
      // ever created for Google to attach a conference to.
      vi.spyOn(api.calendar, 'getConnection').mockResolvedValue(calendarConnection({ exportBookings: false }));
      await renderLoaded(meetPage());

      expect(screen.getByText(/"Export bookings" is switched off/i)).toBeInTheDocument();
      expect(screen.getByRole('link', { name: /turn on booking export/i })).toBeInTheDocument();
    });
  });

  it('reports an unreachable server as connectivity, not as a rejected save', async () => {
    // errorMessage stays real here, which is the point: a failed fetch must not
    // be reported using this screen's own domain wording.
    vi.spyOn(api.organizer, 'updateBookingPageMeetingSettings').mockRejectedValue(new NetworkError());
    const { user } = await renderLoaded();

    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not reach|unreachable|connect/i);
  });

  it('states that the change applies to new bookings only', async () => {
    await renderLoaded();

    expect(screen.getByText(/applies to new bookings/i)).toBeInTheDocument();
  });
});

/**
 * Both requests together, because `meetReadiness` reads a null connection as
 * "not connected" - so a silently failed calendar request would have had the
 * screen assert an outcome it never looked up. Previously it just skeletoned.
 */
describe('MeetingSettingsPage — the settings could not be loaded', () => {
  const PAGE_ID = '11111111-1111-1111-1111-111111111111';
  const renderPage = () =>
    renderWithProviders(<MeetingSettingsPage />, {
      route: `/dashboard/${PAGE_ID}/settings/meeting`,
      path: '/dashboard/:pageId/settings/meeting',
    });

  beforeEach(() => {
    resetAuthState();
  });

  it('reports the failure instead of offering a choice it could not read', async () => {
    vi.spyOn(api.organizer, 'getBookingPage').mockResolvedValue(bookingPageDetail());
    vi.spyOn(api.calendar, 'getConnection').mockRejectedValue(new NetworkError(new Error('down')));
    renderPage();

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.queryByRole('radio', { name: /google meet/i })).not.toBeInTheDocument();
    expect(screen.queryByRole('status', { name: 'Loading' })).not.toBeInTheDocument();
  });

  it('retries and renders the choice when the retry succeeds', async () => {
    vi.spyOn(api.organizer, 'getBookingPage').mockResolvedValue(bookingPageDetail());
    const getConnection = vi.spyOn(api.calendar, 'getConnection')
      .mockRejectedValueOnce(new NetworkError(new Error('down')))
      .mockResolvedValue(calendarConnection());
    const { user } = renderPage();

    await screen.findByRole('alert');
    await user.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByRole('radio', { name: /google meet/i })).toBeInTheDocument();
    expect(getConnection).toHaveBeenCalledTimes(2);
  });
});
