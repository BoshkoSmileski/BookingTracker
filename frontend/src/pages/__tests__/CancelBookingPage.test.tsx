import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { CancelBookingPage } from '../CancelBookingPage';
import { NetworkError, api } from '../../lib/api';
import { bookingConfirmation, publicBooking } from '../../test/factories';
import { renderWithProviders } from '../../test/render';

/**
 * Cancelling, and specifically what the screen is entitled to claim afterwards.
 *
 * Sending is asynchronous: cancelling writes an `EmailNotifications` row and
 * `EmailQueueProcessor` transmits it seconds later, out of the request. So the
 * screen may say "on its way" - never "sent" - and only when the response says a
 * guest confirmation was actually queued. It used to say it whenever the booking
 * carried an email address, which is a different fact: an organizer with guest
 * notifications switched off produces no email at all.
 */
describe('CancelBookingPage', () => {
  const TOKEN = 'public-token-abc';

  const renderPage = () =>
    renderWithProviders(<CancelBookingPage />, { route: `/manage/${TOKEN}/cancel`, path: '/manage/:token/cancel' });

  async function cancel(queued: boolean) {
    vi.spyOn(api.bookings, 'getByToken').mockResolvedValue(publicBooking());
    vi.spyOn(api.bookings, 'cancel').mockResolvedValue(
      bookingConfirmation({ status: 'Cancelled', guestConfirmationQueued: queued }),
    );
    const { user } = renderPage();

    await user.click(await screen.findByRole('button', { name: 'Cancel booking' }));
    await screen.findByRole('heading', { name: /booking cancelled/i });
  }

  it('says a confirmation is on its way when one was queued', async () => {
    await cancel(true);

    expect(screen.getByText(/on its way to jane@example\.com/i)).toBeInTheDocument();
    // Never past tense: nothing has been transmitted at this point.
    expect(screen.queryByText(/we've emailed|has been sent/i)).not.toBeInTheDocument();
  });

  it('promises no email when none was queued, but still confirms the cancellation', async () => {
    await cancel(false);

    expect(screen.getByText(/we've let demo organizer know\.?$/i)).toBeInTheDocument();
    expect(screen.queryByText(/on its way/i)).not.toBeInTheDocument();
  });

  it('offers the organizer’s address once the booking is gone', async () => {
    await cancel(true);

    expect(screen.getByRole('link', { name: 'organizer@example.com' })).toHaveAttribute(
      'href',
      'mailto:organizer@example.com',
    );
  });

  it('reports a failed cancellation rather than claiming the booking is gone', async () => {
    vi.spyOn(api.bookings, 'getByToken').mockResolvedValue(publicBooking());
    vi.spyOn(api.bookings, 'cancel').mockRejectedValue(new NetworkError());
    const { user } = renderPage();

    await user.click(await screen.findByRole('button', { name: 'Cancel booking' }));

    expect(await screen.findByText(/could not reach the server/i)).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: /booking cancelled/i })).not.toBeInTheDocument();
  });
});
