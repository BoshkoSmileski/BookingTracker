import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { SuccessStep } from '../SuccessStep';
import { bookingConfirmation, bookingPage } from '../../../test/factories';
import { renderWithProviders } from '../../../test/render';

/**
 * What the last screen of the wizard is allowed to promise.
 *
 * Submitting queues a confirmation; EmailQueueProcessor sends it seconds later,
 * out of the request, and may retry or fail. "on its way" is therefore the
 * strongest honest tense - and it is conditional on the server saying something
 * was actually queued, which is not the same as the booking having an address on
 * it. Meeting behaviour is covered separately in MeetingInWizard.
 */
describe('SuccessStep confirmation wording', () => {
  const render = (confirmation: ReturnType<typeof bookingConfirmation>) =>
    renderWithProviders(<SuccessStep page={bookingPage()} confirmation={confirmation} />);

  it('names the address the confirmation is on its way to', () => {
    render(bookingConfirmation({ email: 'jane@example.com', guestConfirmationQueued: true }));

    expect(screen.getByText(/on its way to/i)).toBeInTheDocument();
    expect(screen.getByText('jane@example.com')).toBeInTheDocument();
  });

  it('promises no email when the organizer has guest notifications switched off', () => {
    // The booking is still confirmed - only the notification is absent.
    render(bookingConfirmation({ email: 'jane@example.com', guestConfirmationQueued: false }));

    expect(screen.queryByText(/on its way/i)).not.toBeInTheDocument();
    expect(screen.getByText(/your appointment is booked/i)).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /booking confirmed/i })).toBeInTheDocument();
  });

  it('promises no email when queueing failed, rather than reporting the booking as failed', () => {
    // Queueing is best-effort and never fails a booking, so the handler returns
    // false and the screen simply says less.
    render(bookingConfirmation({ email: 'jane@example.com', guestConfirmationQueued: false }));

    expect(screen.getByText('BK-12345')).toBeInTheDocument();
    expect(screen.queryByText(/on its way/i)).not.toBeInTheDocument();
  });

  it('never claims an email was sent, only that one is coming', () => {
    render(bookingConfirmation({ guestConfirmationQueued: true }));

    expect(screen.queryByText(/we've emailed|has been sent|email sent/i)).not.toBeInTheDocument();
  });
});
