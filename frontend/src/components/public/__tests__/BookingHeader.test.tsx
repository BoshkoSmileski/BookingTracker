import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { BookingHeader } from '../BookingHeader';
import { renderWithProviders as render } from '../../../test/render';

/**
 * The persistent answer to "what am I booking?".
 *
 * This replaced the wizard's first step, which asked a guest to "select a
 * service" on a page offering exactly one, and it is the alternative to
 * repeating a summary card on every subsequent step - the header itself
 * absorbs the choice as it is made.
 */
function renderHeader(overrides: Partial<Parameters<typeof BookingHeader>[0]> = {}) {
  const onChangeSelection = vi.fn();
  const result = render(
    <BookingHeader
      organizerName="Demo Organizer"
      title="30 Minute Meeting"
      description="A quick chat"
      durationMinutes={30}
      meetingProvider="GoogleMeet"
      timeZoneId="Europe/Skopje"
      onChangeSelection={onChangeSelection}
      {...overrides}
    />,
  );
  return { ...result, onChangeSelection };
}

describe('BookingHeader', () => {
  it('makes the thing being booked the heading, and the organizer context above it', () => {
    // It used to be the other way round: the organizer's name was the <h1> and
    // the service an <h2> inside a card styled as though it were selectable.
    renderHeader();

    expect(screen.getByRole('heading', { level: 1, name: '30 Minute Meeting' })).toBeInTheDocument();
    expect(screen.getByText('Demo Organizer')).toBeInTheDocument();
  });

  it('states duration, where it happens, and whose clock the times are on', () => {
    renderHeader();

    expect(screen.getByText('30 minutes')).toBeInTheDocument();
    expect(screen.getByText('Google Meet')).toBeInTheDocument();
    expect(screen.getByText(/Europe\/Skopje \(GMT[+-]\d/)).toBeInTheDocument();
  });

  it('says "In person" for a page the organizer has configured that way', () => {
    renderHeader({ meetingProvider: 'None' });

    expect(screen.getByText('In person')).toBeInTheDocument();
  });

  it('states no location at all when the booking carries none', () => {
    // REGRESSION, found by running the app: `null` is not "In person". A
    // booking page always has a real setting, but a *booking* records one only
    // once a meeting exists - so a Google Meet booking made while the organizer
    // had no connected calendar, and any booking whose link the backend
    // withholds, arrive as null. Labelling those "In person" tells a guest
    // where to turn up on no evidence at all.
    renderHeader({ meetingProvider: null });

    expect(screen.queryByText('In person')).not.toBeInTheDocument();
    expect(screen.queryByText('Google Meet')).not.toBeInTheDocument();
    // The rest of the header is unaffected.
    expect(screen.getByText('30 minutes')).toBeInTheDocument();
  });

  it('shows nothing about a slot until one is chosen', () => {
    renderHeader();

    expect(screen.queryByRole('button', { name: /change/i })).not.toBeInTheDocument();
  });

  it('absorbs the chosen slot, with the full day and the exact window', () => {
    renderHeader({
      selection: { date: '2026-08-20', startTime: '10:00:00', endTime: '10:30:00' },
    });

    const expectedDate = new Date(2026, 7, 20).toLocaleDateString(undefined, {
      weekday: 'long', day: 'numeric', month: 'long', year: 'numeric',
    });
    expect(screen.getByText(new RegExp(expectedDate.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')))).toBeInTheDocument();
    expect(screen.getByText(/10:00 – 10:30/)).toBeInTheDocument();
  });

  it('lets the guest change the slot from anywhere in the flow', async () => {
    const { user, onChangeSelection } = renderHeader({
      selection: { date: '2026-08-20', startTime: '10:00:00', endTime: '10:30:00' },
    });

    await user.click(screen.getByRole('button', { name: /change/i }));

    expect(onChangeSelection).toHaveBeenCalled();
  });

  it('drops the description once a slot exists', () => {
    // It is what helps someone decide whether to book, and noise to someone who
    // already has.
    const { rerender } = renderHeader();
    expect(screen.getByText('A quick chat')).toBeInTheDocument();

    rerender(
      <BookingHeader
        organizerName="Demo Organizer"
        title="30 Minute Meeting"
        description="A quick chat"
        durationMinutes={30}
        meetingProvider="GoogleMeet"
        timeZoneId="Europe/Skopje"
        selection={{ date: '2026-08-20', startTime: '10:00:00', endTime: '10:30:00' }}
      />,
    );
    expect(screen.queryByText('A quick chat')).not.toBeInTheDocument();
  });

  it('adds the visitor\'s own reading of the slot when the zones differ', () => {
    renderHeader({
      timeZoneId: 'Pacific/Kiritimati',
      selection: {
        date: '2026-08-20',
        startTime: '10:00:00',
        endTime: '10:30:00',
        startUtc: '2026-08-20T07:00:00Z',
        endUtc: '2026-08-20T07:30:00Z',
      },
    });

    expect(screen.getByText(/your time/)).toBeInTheDocument();
  });

  it('says nothing about "your time" for a visitor already in that zone', () => {
    renderHeader({
      timeZoneId: Intl.DateTimeFormat().resolvedOptions().timeZone,
      selection: {
        date: '2026-08-20',
        startTime: '10:00:00',
        endTime: '10:30:00',
        startUtc: '2026-08-20T07:00:00Z',
        endUtc: '2026-08-20T07:30:00Z',
      },
    });

    expect(screen.queryByText(/your time/)).not.toBeInTheDocument();
  });
});
