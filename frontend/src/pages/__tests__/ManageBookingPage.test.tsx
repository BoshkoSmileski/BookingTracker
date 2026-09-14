import { screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ManageBookingPage } from '../ManageBookingPage';
import { NetworkError, api } from '../../lib/api';
import { publicBooking } from '../../test/factories';
import { renderWithProviders } from '../../test/render';

/**
 * The guest's own view of their booking - the only screen in the app an
 * organizer's customers reach while signed out. Its job in this feature is to
 * offer the meeting on the day, which is exactly what a guest opens this page
 * for once the booking itself is settled.
 */
describe('ManageBookingPage', () => {
  const TOKEN = 'public-token-abc';
  const MEET_URL = 'https://meet.google.com/abc-defg-hij';

  const renderPage = () =>
    renderWithProviders(<ManageBookingPage />, { route: `/manage/${TOKEN}`, path: '/manage/:token' });

  it('renders the booking details', async () => {
    vi.spyOn(api.bookings, 'getByToken').mockResolvedValue(publicBooking());
    renderPage();

    expect(await screen.findByText('BK-12345')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: '30 Minute Meeting' })).toBeInTheDocument();
  });

  it('states the date, the time window and whose clock they are on', async () => {
    // Raw ISO values - "2026-08-20" and "09:00" - used to be printed verbatim.
    vi.spyOn(api.bookings, 'getByToken').mockResolvedValue(
      publicBooking({ selectedDate: '2026-08-20', selectedTime: '09:00:00', durationMinutes: 30 }),
    );
    renderPage();

    await screen.findByText('BK-12345');
    // Derived from the same formatter the code uses, never a hardcoded locale
    // string: a fixed "Thursday" fails on any machine with another locale.
    const expectedDate = new Date(2026, 7, 20).toLocaleDateString(undefined, {
      weekday: 'long', day: 'numeric', month: 'long', year: 'numeric',
    });
    expect(screen.getByText(new RegExp(expectedDate.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')))).toBeInTheDocument();
    expect(screen.getByText(/09:00 – 09:30/)).toBeInTheDocument();
    expect(screen.getByText(/Europe\/Skopje/)).toBeInTheDocument();
  });

  it('offers the meeting when the booking has one', async () => {
    vi.spyOn(api.bookings, 'getByToken').mockResolvedValue(
      publicBooking({ meetingProvider: 'GoogleMeet', meetingUrl: MEET_URL }),
    );
    renderPage();

    expect(await screen.findByRole('link', { name: 'Join Google Meet' })).toHaveAttribute('href', MEET_URL);
    // "Google Meet" legitimately appears twice - as the booking's location in
    // the header, and as the title of the panel that joins it - so the
    // assertion is scoped rather than loosened.
    const panel = screen.getByRole('group', { name: 'Online meeting' });
    expect(within(panel).getByText('Google Meet')).toBeInTheDocument();
    expect(within(panel).getByText(MEET_URL)).toBeInTheDocument();
  });

  it('shows no meeting, and claims no location, for a booking that carries neither', async () => {
    // The DTO sends `meetingProvider: null` both for a booking that never got a
    // meeting and for one whose link is being withheld. Neither is evidence
    // that the guest should turn up somewhere in person, so the header says
    // nothing about where rather than guessing.
    vi.spyOn(api.bookings, 'getByToken').mockResolvedValue(publicBooking());
    renderPage();

    await screen.findByText('BK-12345');
    expect(screen.queryByRole('link', { name: /^join/i })).not.toBeInTheDocument();
    expect(screen.queryByText('In person')).not.toBeInTheDocument();
  });

  it('shows no meeting once the booking is cancelled', async () => {
    // The backend withholds the link for a booking that is no longer
    // manageable, because its Google event - and with it the live Meet - has
    // been deleted. The page simply renders what it is given.
    vi.spyOn(api.bookings, 'getByToken').mockResolvedValue(
      publicBooking({ status: 'Cancelled', canCancel: false, canReschedule: false, meetingProvider: null, meetingUrl: null }),
    );
    renderPage();

    await screen.findByText('BK-12345');
    expect(screen.queryByRole('link', { name: /^join/i })).not.toBeInTheDocument();
    expect(screen.getByText(/can no longer be changed/i)).toBeInTheDocument();
  });

  it('still offers reschedule and cancel alongside the meeting', async () => {
    // The meeting panel sits between the details and the manage actions; it
    // must not displace them.
    vi.spyOn(api.bookings, 'getByToken').mockResolvedValue(
      publicBooking({ meetingProvider: 'GoogleMeet', meetingUrl: MEET_URL }),
    );
    renderPage();

    expect(await screen.findByRole('link', { name: 'Join Google Meet' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Reschedule' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Cancel booking' })).toBeInTheDocument();
  });

  describe('reaching the organizer', () => {
    it('offers the organizer’s address as a mailto link', async () => {
      vi.spyOn(api.bookings, 'getByToken').mockResolvedValue(
        publicBooking({ organizerName: 'Demo Organizer', organizerEmail: 'alex@organizer.example' }),
      );
      renderPage();

      const link = await screen.findByRole('link', { name: 'alex@organizer.example' });
      expect(link).toHaveAttribute('href', 'mailto:alex@organizer.example');
    });

    it('still offers it once the booking can no longer be changed', async () => {
      // The state the field exists for. This screen used to end on "contact the
      // organizer directly" with nothing anywhere on the page saying how.
      vi.spyOn(api.bookings, 'getByToken').mockResolvedValue(
        publicBooking({ status: 'Cancelled', canCancel: false, canReschedule: false }),
      );
      renderPage();

      expect(await screen.findByText(/can no longer be changed/i)).toBeInTheDocument();
      expect(screen.getByRole('link', { name: 'organizer@example.com' })).toHaveAttribute(
        'href',
        'mailto:organizer@example.com',
      );
    });
  });

  describe('add to calendar', () => {
    /**
     * Captures the blob handed to the browser without letting jsdom try to
     * navigate to the object URL.
     *
     * Stubs the anchor's `click`, not `document.createElement` - React creates
     * elements throughout a render, so intercepting that returns the same node
     * for everything and the tree fails to build.
     */
    function captureDownload() {
      vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
      const create = vi.spyOn(URL, 'createObjectURL');
      return () => create.mock.calls[0][0] as Blob;
    }

    it('builds the calendar file from the instant the backend resolved, not from the wall clock', async () => {
      // The regression this exists for: 09:00 is the *organizer's* clock, and
      // reading it as the visitor's would have produced a different moment.
      // Europe/Skopje puts that 09:00 at 07:00Z, which is what must land in the
      // file.
      const readBlob = captureDownload();
      vi.spyOn(api.bookings, 'getByToken').mockResolvedValue(
        publicBooking({
          selectedTime: '09:00:00',
          timeZoneId: 'Europe/Skopje',
          startUtc: '2026-08-20T07:00:00Z',
          endUtc: '2026-08-20T07:30:00Z',
        }),
      );
      const { user } = renderPage();

      await user.click(await screen.findByRole('button', { name: /add to calendar/i }));

      const ics = await readBlob().text();
      expect(ics).toContain('DTSTART:20260820T070000Z');
      expect(ics).toContain('DTEND:20260820T073000Z');
      // The wall-clock reading, which is what reconstructing it here would give
      // a visitor whose browser is on UTC.
      expect(ics).not.toContain('DTSTART:20260820T090000Z');
    });

    it('names the booking, so the calendar entry is not just a blob of ISO values', async () => {
      const readBlob = captureDownload();
      vi.spyOn(api.bookings, 'getByToken').mockResolvedValue(publicBooking());
      const { user } = renderPage();

      await user.click(await screen.findByRole('button', { name: /add to calendar/i }));

      const ics = await readBlob().text();
      expect(ics).toContain('SUMMARY:30 Minute Meeting with Demo Organizer');
      expect(ics).toContain('BK-12345');
    });

    it('is not offered for a booking that can no longer be managed', async () => {
      vi.spyOn(api.bookings, 'getByToken').mockResolvedValue(
        publicBooking({ status: 'Cancelled', canCancel: false, canReschedule: false }),
      );
      renderPage();

      await screen.findByText(/can no longer be changed/i);
      expect(screen.queryByRole('button', { name: /add to calendar/i })).not.toBeInTheDocument();
    });
  });

  describe('what the page is allowed to promise', () => {
    it('states the real cancellation cutoff, not a generic policy', async () => {
      // canCancel/canReschedule are computed server-side as "still Submitted and
      // the start is still in the future", so the cutoff IS the appointment
      // start. Anything looser would be a policy the backend does not enforce.
      vi.spyOn(api.bookings, 'getByToken').mockResolvedValue(publicBooking());
      renderPage();

      expect(await screen.findByText(/any time before it starts/i)).toBeInTheDocument();
      expect(screen.queryByText(/cancel anytime|cancel at any time/i)).not.toBeInTheDocument();
    });

    it('says nothing about a cutoff once the booking can no longer be changed', async () => {
      vi.spyOn(api.bookings, 'getByToken').mockResolvedValue(
        publicBooking({ status: 'Cancelled', canCancel: false, canReschedule: false }),
      );
      renderPage();

      await screen.findByText(/can no longer be changed/i);
      expect(screen.queryByText(/before it starts/i)).not.toBeInTheDocument();
    });

    it('mentions a reminder only when the backend says one is actually scheduled', async () => {
      vi.spyOn(api.bookings, 'getByToken').mockResolvedValue(
        publicBooking({ reminderLeadMinutes: [1440] }),
      );
      renderPage();

      expect(await screen.findByText(/we.ll email you a reminder 24 hours before/i)).toBeInTheDocument();
    });

    it('lists every scheduled lead time, in the order they will arrive', async () => {
      vi.spyOn(api.bookings, 'getByToken').mockResolvedValue(
        publicBooking({ reminderLeadMinutes: [60, 1440] }),
      );
      renderPage();

      expect(await screen.findByText(/1 hour and 24 hours before/i)).toBeInTheDocument();
    });

    it('promises no reminder when none is scheduled', async () => {
      // Reminders switched off, cancelled with the booking, or already sent -
      // all arrive here as an empty list, and none of them may be reported as
      // a reminder still to come.
      vi.spyOn(api.bookings, 'getByToken').mockResolvedValue(publicBooking({ reminderLeadMinutes: [] }));
      renderPage();

      await screen.findByText('BK-12345');
      expect(screen.queryByText(/remind/i)).not.toBeInTheDocument();
    });
  });

  it('does not dress a cancelled booking as a live one', async () => {
    // The selection line is the accent panel on a live booking. A cancelled one
    // wearing it made "you are booked" and "this was cancelled" look identical.
    vi.spyOn(api.bookings, 'getByToken').mockResolvedValue(
      publicBooking({ status: 'Cancelled', canCancel: false, canReschedule: false }),
    );
    const { container } = renderPage();

    await screen.findByText(/can no longer be changed/i);
    expect(container.querySelector('.bg-accent-50')).toBeNull();
    expect(container.querySelector('.bg-gray-100')).not.toBeNull();
  });

  it('keeps the accent for a booking that is still live', async () => {
    vi.spyOn(api.bookings, 'getByToken').mockResolvedValue(publicBooking());
    const { container } = renderPage();

    await screen.findByText('BK-12345');
    expect(container.querySelector('.bg-accent-50')).not.toBeNull();
  });

  it('keeps the layout while loading instead of blanking the screen', async () => {
    let resolve: (b: ReturnType<typeof publicBooking>) => void = () => {};
    vi.spyOn(api.bookings, 'getByToken').mockReturnValue(
      new Promise((r) => {
        resolve = r;
      }),
    );
    renderPage();

    expect(screen.getByRole('status', { name: 'Loading' })).toBeInTheDocument();
    resolve(publicBooking());
    expect(await screen.findByText('BK-12345')).toBeInTheDocument();
  });

  it('reports an unreachable server as connectivity rather than a missing booking', async () => {
    vi.spyOn(api.bookings, 'getByToken').mockRejectedValue(new NetworkError());
    renderPage();

    expect(await screen.findByText(/could not reach the server/i)).toBeInTheDocument();
  });
});
