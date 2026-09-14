import { screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SessionDetailPage } from '../SessionDetailPage';
import { ApiError, NetworkError, api } from '../../lib/api';
import { bookingFormField, bookingPageDetail, bookingSession, reminder, sessionEvent } from '../../test/factories';
import { formatDateTime } from '../../lib/dates';
import { renderWithProviders } from '../../test/render';
import { resetAuthState } from '../../test/authContextMock';

vi.mock('../../contexts/AuthContext', () => import('../../test/authContextMock'));
// SignalR opens a real socket on mount; the page's live updates are out of scope
// here, so the connection is stubbed to a no-op rather than left to fail noisily.
vi.mock('../../lib/signalr', () => ({
  createDashboardConnection: () => ({
    on: () => {},
    start: () => Promise.resolve(),
    invoke: () => Promise.resolve(),
    stop: () => Promise.resolve(),
  }),
}));

describe('SessionDetailPage', () => {
  const PAGE_ID = '11111111-1111-1111-1111-111111111111';
  const SESSION_ID = '22222222-2222-2222-2222-222222222222';

  const renderPage = () =>
    renderWithProviders(<SessionDetailPage />, {
      route: `/dashboard/${PAGE_ID}/sessions/${SESSION_ID}`,
      path: '/dashboard/:pageId/sessions/:sessionId',
    });

  function mockAll(over: {
    session?: ReturnType<typeof bookingSession>;
    events?: ReturnType<typeof sessionEvent>[];
    reminders?: ReturnType<typeof reminder>[];
    page?: ReturnType<typeof bookingPageDetail>;
  } = {}) {
    vi.spyOn(api.organizer, 'getSession').mockResolvedValue(over.session ?? bookingSession());
    vi.spyOn(api.organizer, 'getTimeline').mockResolvedValue(over.events ?? [
      sessionEvent(),
      sessionEvent({ id: 2, eventType: 'BookingSubmitted', clientSequenceNumber: 5 }),
    ]);
    // The page detail rather than the page list: this screen needs the slug
    // (to reschedule) and the custom fields (to label the answers), and one
    // request carries both.
    vi.spyOn(api.organizer, 'getBookingPage').mockResolvedValue(over.page ?? bookingPageDetail());
    vi.spyOn(api.organizer, 'getSessionReminders').mockResolvedValue(over.reminders ?? []);
  }

  beforeEach(() => {
    resetAuthState();
  });

  it('renders the guest details once loaded', async () => {
    mockAll();
    renderPage();

    // The name is the page title now, at the same size as every other screen's,
    // rather than a hand-rolled 16px heading one step below its own sections.
    expect(await screen.findByRole('heading', { level: 1, name: 'Jane Doe' })).toBeInTheDocument();
    // Twice on purpose: the header states it, and the detail grid lists it
    // beside phone/date/time.
    expect(screen.getAllByText('jane@example.com').length).toBeGreaterThan(0);
  });

  it('reports a failed load instead of skeletoning for ever', async () => {
    // `refresh` had no catch at all, so an error left the loading state on
    // screen permanently - indistinguishable from a slow request.
    mockAll();
    vi.spyOn(api.organizer, 'getSession').mockRejectedValue(new NetworkError());
    renderPage();

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not reach the server/i);
    expect(screen.queryByLabelText('Loading')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: /try again/i })).toBeInTheDocument();
  });

  it('renders the event timeline', async () => {
    mockAll();
    renderPage();

    expect(await screen.findByText('Booking page opened')).toBeInTheDocument();
    expect(screen.getByText('Booking submitted')).toBeInTheDocument();
  });

  it('shows the reminders panel with per-reminder status', async () => {
    mockAll({
      reminders: [
        reminder({ id: 'r1', label: '24 hours', status: 'Sent', sentAtUtc: '2026-08-19T07:00:05' }),
        reminder({ id: 'r2', label: '1 hour', minutesBeforeEvent: 60, status: 'Scheduled' }),
      ],
    });
    renderPage();

    expect(await screen.findByRole('heading', { name: 'Reminders' })).toBeInTheDocument();
    // Scoped by landmark rather than by `.parentElement`, which was pinned to
    // one particular nesting depth of the markup.
    const panel = screen.getByRole('region', { name: 'Reminders' });
    expect(within(panel).getByText('24 hours before')).toBeInTheDocument();
    expect(within(panel).getByText('Upcoming')).toBeInTheDocument();
  });

  it('hides the reminders panel entirely when there are none', async () => {
    mockAll({ reminders: [] });
    renderPage();
    await screen.findByText('Jane Doe');

    expect(screen.queryByRole('heading', { name: 'Reminders' })).not.toBeInTheDocument();
  });

  it('loads email history on demand and renders timestamps through the shared UTC parser', async () => {
    // REGRESSION: these timestamps previously had an inline `${value}Z` workaround
    // instead of going through lib/dates.ts.
    mockAll();
    vi.spyOn(api.organizer, 'getEmailHistory').mockResolvedValue([
      sessionEvent({ id: 9, eventType: 'EmailSent', fieldName: 'BookingConfirmation', newValue: 'jane@example.com', timestamp: '2026-08-04T18:15:00' }),
    ]);
    const { user } = renderPage();
    await screen.findByText('Jane Doe');

    await user.click(screen.getByRole('button', { name: /view email history/i }));

    expect(await screen.findByText(/confirmation email sent to jane@example.com/i)).toBeInTheDocument();
    expect(screen.getByText(formatDateTime('2026-08-04T18:15:00'))).toBeInTheDocument();
  });

  it('toggles email history closed again', async () => {
    mockAll();
    vi.spyOn(api.organizer, 'getEmailHistory').mockResolvedValue([]);
    const { user } = renderPage();
    await screen.findByText('Jane Doe');

    await user.click(screen.getByRole('button', { name: /view email history/i }));
    expect(await screen.findByText(/nothing delivered yet/i)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /hide email history/i }));
    expect(screen.queryByText(/nothing delivered yet/i)).not.toBeInTheDocument();
  });

  it('reports a failed email-history load rather than looking like an empty one', async () => {
    // REGRESSION: `toggleEmailHistory` had a `finally` but no `catch`, so a
    // rejection just put the button back to "View email history" with nothing
    // shown - indistinguishable from a booking that has sent no emails, which
    // is the opposite conclusion to draw.
    mockAll();
    vi.spyOn(api.organizer, 'getEmailHistory').mockRejectedValue(new NetworkError());
    const { user } = renderPage();
    await screen.findByText('Jane Doe');

    await user.click(screen.getByRole('button', { name: /view email history/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not reach the server/i);
    expect(screen.queryByText(/nothing delivered yet/i)).not.toBeInTheDocument();
  });

  it('offers management actions for a confirmed booking', async () => {
    mockAll({ session: bookingSession({ status: 'Submitted' }) });
    renderPage();

    expect(await screen.findByRole('button', { name: /cancel booking/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /resend confirmation/i })).toBeInTheDocument();
  });

  it('hides management actions for a session that is not a confirmed booking', async () => {
    mockAll({ session: bookingSession({ status: 'Abandoned', submittedAt: null }) });
    renderPage();
    await screen.findByText('Jane Doe');

    expect(screen.queryByRole('button', { name: /cancel booking/i })).not.toBeInTheDocument();
  });

  it('resends the confirmation and reports success', async () => {
    mockAll();
    const resend = vi.spyOn(api.organizer, 'resendConfirmation').mockResolvedValue(undefined);
    const { user } = renderPage();
    await screen.findByText('Jane Doe');

    await user.click(screen.getByRole('button', { name: /resend confirmation/i }));

    await waitFor(() => expect(resend).toHaveBeenCalledWith(expect.any(String), PAGE_ID, SESSION_ID));
    // "Confirmation email resent." claimed a delivery the app had not made:
    // ResendConfirmationCommandHandler queues, and EmailQueueProcessor sends
    // later. The only surface allowed to say "sent" is the email history, which
    // is built from events written after a send succeeded.
    const notice = await screen.findByText(/confirmation email queued/i);
    expect(notice).toHaveTextContent(/check the email history/i);
    expect(screen.queryByText(/email resent\./i)).not.toBeInTheDocument();
  });

  it('does not claim a cancellation email was sent when it was only queued', async () => {
    mockAll();
    vi.spyOn(api.organizer, 'cancelSession').mockResolvedValue({
      ...bookingSession({ status: 'Cancelled' }), cancelledAt: '2026-08-04T12:00:00',
      rescheduledAt: null, bookingReference: 'R', publicToken: 't', guestConfirmationQueued: true,
    });
    const { user } = renderPage();
    await screen.findByRole('heading', { level: 1, name: 'Jane Doe' });

    await user.click(screen.getByRole('button', { name: /cancel booking/i }));
    await user.click(within(await screen.findByRole('group', { name: /cancel this booking/i }))
      .getByRole('button', { name: /cancel booking/i }));

    expect(await screen.findByText(/notification emails are queued/i)).toBeInTheDocument();
  });

  it('clears a previous success before the next action reports', async () => {
    // A stale "Booking rescheduled" used to sit beside a fresh failure.
    mockAll();
    const resend = vi.spyOn(api.organizer, 'resendConfirmation').mockResolvedValue(undefined);
    const { user } = renderPage();
    await screen.findByRole('heading', { level: 1, name: 'Jane Doe' });

    await user.click(screen.getByRole('button', { name: /resend confirmation/i }));
    await screen.findByText(/confirmation email queued/i);

    resend.mockRejectedValue(new ApiError(400, { title: 'This session has no confirmed booking to resend.' }));
    await user.click(screen.getByRole('button', { name: /resend confirmation/i }));

    await screen.findByText(/no confirmed booking to resend/i);
    expect(screen.queryByText(/confirmation email queued/i)).not.toBeInTheDocument();
  });

  it('reports the server’s reason when an action is rejected', async () => {
    mockAll();
    vi.spyOn(api.organizer, 'resendConfirmation')
      .mockRejectedValue(new ApiError(400, { title: 'This session has no confirmed booking to resend.' }));
    const { user } = renderPage();
    await screen.findByText('Jane Doe');

    await user.click(screen.getByRole('button', { name: /resend confirmation/i }));

    expect(await screen.findByText(/no confirmed booking to resend/i)).toBeInTheDocument();
  });

  it('cancels a booking through the confirmation form and refreshes', async () => {
    mockAll();
    const cancel = vi.spyOn(api.organizer, 'cancelSession').mockResolvedValue({
      ...bookingSession({ status: 'Cancelled' }), cancelledAt: '2026-08-04T12:00:00',
      rescheduledAt: null, bookingReference: 'R', publicToken: 't', guestConfirmationQueued: true,
    });
    const { user } = renderPage();
    await screen.findByText('Jane Doe');

    await user.click(screen.getByRole('button', { name: /cancel booking/i }));
    // The confirmation is an inline panel, not a browser dialog - it states the
    // consequence and carries the optional reason field inside the question.
    const panel = await screen.findByRole('group', { name: /cancel this booking/i });
    expect(within(panel).getByText(/pending reminders are cancelled/i)).toBeInTheDocument();
    await user.click(within(panel).getByRole('button', { name: /cancel booking/i }));

    await waitFor(() => expect(cancel).toHaveBeenCalled());
  });

  describe('custom question answers', () => {
    it('shows each answer under the question the organizer asked', async () => {
      mockAll({
        page: bookingPageDetail({
          formFields: [
            bookingFormField({ id: 'a', label: 'Company', displayOrder: 0 }),
            bookingFormField({ id: 'b', label: 'Topic', displayOrder: 1 }),
          ],
        }),
        session: bookingSession({
          answers: [
            { fieldId: 'b', value: 'Pricing for next quarter' },
            { fieldId: 'a', value: 'Acme Ltd' },
          ],
        }),
      });

      renderPage();

      const answers = await screen.findByRole('region', { name: /answers/i });
      expect(within(answers).getByText('Company')).toBeInTheDocument();
      expect(within(answers).getByText('Acme Ltd')).toBeInTheDocument();
      expect(within(answers).getByText('Topic')).toBeInTheDocument();
      expect(within(answers).getByText('Pricing for next quarter')).toBeInTheDocument();
    });

    it('shows answers in the organizer display order, not the order the API returned them', async () => {
      mockAll({
        page: bookingPageDetail({
          formFields: [
            bookingFormField({ id: 'a', label: 'Asked second', displayOrder: 1 }),
            bookingFormField({ id: 'b', label: 'Asked first', displayOrder: 0 }),
          ],
        }),
        session: bookingSession({
          answers: [
            { fieldId: 'a', value: 'Second answer' },
            { fieldId: 'b', value: 'First answer' },
          ],
        }),
      });

      renderPage();

      const answers = await screen.findByRole('region', { name: /answers/i });
      const labels = within(answers).getAllByRole('term').map((el) => el.textContent);
      expect(labels).toEqual(['Asked first', 'Asked second']);
    });

    it('shows no answers panel at all for a booking page that asks nothing', async () => {
      mockAll();
      renderPage();

      await screen.findByText('Jane Doe');
      expect(screen.queryByRole('region', { name: /answers/i })).not.toBeInTheDocument();
    });
  });

  describe('meeting', () => {
    const MEET_URL = 'https://meet.google.com/abc-defg-hij';

    it('shows the meeting with a join link when the booking has one', async () => {
      mockAll({ session: bookingSession({ meetingProvider: 'GoogleMeet', meetingUrl: MEET_URL }) });
      renderPage();

      const meeting = await screen.findByRole('region', { name: /meeting/i });
      expect(within(meeting).getByText('Google Meet')).toBeInTheDocument();
      expect(within(meeting).getByRole('link', { name: 'Join Google Meet' })).toHaveAttribute('href', MEET_URL);
      expect(within(meeting).getByRole('button', { name: 'Copy link' })).toBeInTheDocument();
    });

    it('shows no meeting panel for an in-person booking', async () => {
      mockAll();
      renderPage();

      await screen.findByText('Jane Doe');
      expect(screen.queryByRole('region', { name: /meeting/i })).not.toBeInTheDocument();
    });

    it('shows no meeting panel when a link was wanted but never created', async () => {
      // Google unavailable, or no connected calendar, when the booking was
      // made. A heading over an empty panel would read as a broken link.
      mockAll({ session: bookingSession({ meetingProvider: 'GoogleMeet', meetingUrl: null }) });
      renderPage();

      await screen.findByText('Jane Doe');
      expect(screen.queryByRole('region', { name: /meeting/i })).not.toBeInTheDocument();
    });

    it('describes the meeting-link event in the timeline without repeating the URL', async () => {
      mockAll({
        session: bookingSession({ meetingProvider: 'GoogleMeet', meetingUrl: MEET_URL }),
        events: [sessionEvent({ id: 3, eventType: 'MeetingLinkAssigned', fieldName: 'GoogleMeet', newValue: MEET_URL })],
      });
      renderPage();

      expect(await screen.findByText('Google Meet link created')).toBeInTheDocument();
    });
  });
});
