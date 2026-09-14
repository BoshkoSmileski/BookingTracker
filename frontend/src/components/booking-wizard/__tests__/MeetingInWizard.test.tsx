import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { SuccessStep } from '../SuccessStep';
import { BookingHeader } from '../../public/BookingHeader';
import { bookingConfirmation, bookingPage } from '../../../test/factories';
import { renderWithProviders } from '../../../test/render';

const MEET_URL = 'https://meet.google.com/abc-defg-hij';

/**
 * The two points in the public wizard where a meeting matters: telling a
 * visitor where the meeting happens BEFORE they invest in choosing a slot, and
 * handing them the actual link the moment the booking is made.
 *
 * The "before" half now lives in `BookingHeader`, which is on screen for the
 * whole flow, rather than on a service step that was shown once and scrolled
 * out of the guest's life after a single click.
 */
describe('meeting in the booking wizard', () => {
  function renderHeader(page: ReturnType<typeof bookingPage>) {
    return renderWithProviders(
      <BookingHeader
        organizerName={page.organizerName}
        title={page.title}
        description={page.description}
        durationMinutes={page.durationMinutes}
        meetingProvider={page.meetingProvider}
        timeZoneId={page.timeZoneId}
      />,
    );
  }

  describe('BookingHeader', () => {
    it('names the meeting before any time is picked', () => {
      renderHeader(bookingPage({ meetingProvider: 'GoogleMeet' }));

      expect(screen.getByText('Google Meet')).toBeInTheDocument();
    });

    it('says "In person" for a page with no online meeting, rather than staying silent', () => {
      // The organizer's own meeting settings screen calls this "In person", so
      // it is their configured answer to "where is this?" - not an invention.
      renderHeader(bookingPage({ meetingProvider: 'None' }));

      expect(screen.getByText('In person')).toBeInTheDocument();
    });

    it('shows no join link, because none exists until the booking is made', () => {
      // The distinction the DTO encodes: the *kind* of meeting is known up
      // front, but Google only mints the link when the booking creates the
      // calendar event.
      renderHeader(bookingPage({ meetingProvider: 'GoogleMeet' }));

      expect(screen.queryByRole('link', { name: /^join/i })).not.toBeInTheDocument();
    });
  });

  describe('SuccessStep', () => {
    it('offers the meeting straight away, without waiting for the confirmation email', () => {
      renderWithProviders(
        <SuccessStep
          page={bookingPage({ meetingProvider: 'GoogleMeet' })}
          confirmation={bookingConfirmation({ meetingProvider: 'GoogleMeet', meetingUrl: MEET_URL })}
        />,
      );

      expect(screen.getByRole('link', { name: 'Join Google Meet' })).toHaveAttribute('href', MEET_URL);
    });

    it('shows no meeting panel for an in-person booking', () => {
      renderWithProviders(<SuccessStep page={bookingPage()} confirmation={bookingConfirmation()} />);

      expect(screen.queryByRole('link', { name: /^join/i })).not.toBeInTheDocument();
      // The rest of the confirmation is unaffected.
      expect(screen.getByText('BK-12345')).toBeInTheDocument();
    });

    it('degrades to no panel when Google produced no link, keeping the confirmation intact', () => {
      // The booking succeeded; only the meeting is missing. This is the whole
      // point of the best-effort contract.
      renderWithProviders(
        <SuccessStep
          page={bookingPage({ meetingProvider: 'GoogleMeet' })}
          confirmation={bookingConfirmation({ meetingProvider: null, meetingUrl: null })}
        />,
      );

      expect(screen.getByText(/booking confirmed/i)).toBeInTheDocument();
      expect(screen.queryByRole('link', { name: /^join/i })).not.toBeInTheDocument();
    });
  });
});
