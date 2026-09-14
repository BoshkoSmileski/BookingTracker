import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { BookingInstructions } from '../BookingInstructions';
import { ReviewStep } from '../ReviewStep';
import { ScheduleStep } from '../ScheduleStep';
import { bookingInstruction, bookingPage } from '../../../test/factories';

/**
 * REGRESSION AREA: instructions reaching the visitor at all.
 *
 * Until this component existed, an organizer could write instructions and the
 * public booking page never rendered them - the public DTO did not even carry
 * them. These tests exist so that cannot silently regress to storage-only.
 *
 * The two "in the wizard" cases below now render `ScheduleStep` rather than the
 * deleted `ServiceStep`: the claim is unchanged - instructions are read on the
 * first screen, before any time is invested, and again immediately before
 * confirming - only the component carrying the first screen has changed.
 */
describe('BookingInstructions', () => {
  const instructions = [
    bookingInstruction({ id: 'a', text: 'Bring photo ID' }),
    bookingInstruction({ id: 'b', text: 'Arrive five minutes early' }),
  ];

  it('renders each instruction as its own item under the heading', () => {
    render(<BookingInstructions instructions={instructions} heading="Before you book" />);

    const notice = screen.getByRole('region', { name: /booking instructions/i });
    expect(within(notice).getByRole('heading', { name: /before you book/i })).toBeInTheDocument();
    expect(within(notice).getAllByRole('listitem').map((li) => li.textContent))
      .toEqual([expect.stringContaining('Bring photo ID'), expect.stringContaining('Arrive five minutes early')]);
  });

  it('renders nothing at all when the organizer set none', () => {
    const { container } = render(<BookingInstructions instructions={[]} heading="Before you book" />);

    // The wizard must look exactly as it did for pages that don't use the feature.
    expect(container).toBeEmptyDOMElement();
  });

  it('offers the visitor nothing to fill in', () => {
    render(<BookingInstructions instructions={instructions} heading="Before you book" />);

    // The whole point of the rename: these are read, never answered.
    const notice = screen.getByRole('region', { name: /booking instructions/i });
    expect(within(notice).queryByRole('textbox')).not.toBeInTheDocument();
    expect(within(notice).queryByRole('checkbox')).not.toBeInTheDocument();
    expect(notice.querySelector('input, textarea, select')).toBeNull();
  });
});

describe('instructions in the booking wizard', () => {
  const instructions = [bookingInstruction({ text: 'Bring photo ID' })];

  function renderSchedule(pageInstructions: typeof instructions | []) {
    return render(
      <ScheduleStep
        instructions={pageInstructions}
        monthCursor={new Date(2026, 7, 1)}
        onMonthChange={() => {}}
        slots={[]}
        loading={false}
        selectedDateKey={null}
        onSelectDate={() => {}}
        selectedSlot={null}
        onSelectSlot={() => {}}
        timeZoneId="Europe/Skopje"
      />,
    );
  }

  const filled = {
    name: 'Jane Doe', email: 'jane@example.com', phone: '', message: '',
    selectedDate: '2026-08-20', selectedTime: '09:00:00', answers: {},
  };

  function renderReview(page: ReturnType<typeof bookingPage>) {
    return render(
      <ReviewStep
        page={page}
        formState={filled}
        submitting={false}
        submitError={null}
        submitErrorIsConflict={false}
        onConfirm={() => {}}
        onBack={() => {}}
        onEditTime={() => {}}
        onEditDetails={() => {}}
      />,
    );
  }

  it('appears on the first step, before the visitor picks a slot', () => {
    renderSchedule(instructions);

    expect(screen.getByRole('heading', { name: /before you book/i })).toBeInTheDocument();
    expect(screen.getByText('Bring photo ID')).toBeInTheDocument();
  });

  it('is read before the calendar, not after it', () => {
    // "Please have your account number ready" is useless news to someone who
    // has already spent two minutes choosing a slot.
    renderSchedule(instructions);

    const notice = screen.getByRole('region', { name: /booking instructions/i });
    const calendar = screen.getByRole('grid', { name: /choose a date/i });
    expect(notice.compareDocumentPosition(calendar) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('appears again on the review step, just before confirming', () => {
    renderReview(bookingPage({ instructions }));

    expect(screen.getByRole('heading', { name: /before you confirm/i })).toBeInTheDocument();
    expect(screen.getByText('Bring photo ID')).toBeInTheDocument();
  });

  it('leaves both steps untouched when there are no instructions', () => {
    const schedule = renderSchedule([]);
    expect(schedule.queryByRole('region', { name: /booking instructions/i })).not.toBeInTheDocument();
    schedule.unmount();

    renderReview(bookingPage());
    expect(screen.queryByRole('region', { name: /booking instructions/i })).not.toBeInTheDocument();
  });
});
