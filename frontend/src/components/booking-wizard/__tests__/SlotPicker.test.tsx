import { screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { SlotPicker } from '../SlotPicker';
import { renderWithProviders as render } from '../../../test/render';
import type { AvailableSlotDto } from '../../../lib/types';

/**
 * The central interaction of the whole product: choosing when.
 *
 * Date and time used to be two wizard steps that replaced one another, so
 * comparing one day's times against another's meant navigating backwards and
 * losing your place. These tests pin the properties that made merging them
 * worth doing, plus the two things the old components got outright wrong - a
 * fake `role="listbox"` and a time label on the wrong clock.
 */

const MONTH = new Date(2026, 7, 1); // August 2026

function slot(overrides: Partial<AvailableSlotDto> = {}): AvailableSlotDto {
  return {
    localDate: '2026-08-20',
    localStartTime: '09:00:00',
    localEndTime: '09:30:00',
    startUtc: '2026-08-20T07:00:00Z',
    endUtc: '2026-08-20T07:30:00Z',
    ...overrides,
  };
}

function renderPicker(overrides: Partial<Parameters<typeof SlotPicker>[0]> = {}) {
  const onSelectDate = vi.fn();
  const onSelectSlot = vi.fn();
  const onMonthChange = vi.fn();
  const result = render(
    <SlotPicker
      monthCursor={MONTH}
      onMonthChange={onMonthChange}
      slots={[slot()]}
      loading={false}
      selectedDateKey={null}
      onSelectDate={onSelectDate}
      selectedSlot={null}
      onSelectSlot={onSelectSlot}
      timeZoneId="Europe/Skopje"
      {...overrides}
    />,
  );
  return { ...result, onSelectDate, onSelectSlot, onMonthChange };
}

describe('SlotPicker — date and time on one screen', () => {
  it('shows the calendar and the times panel at the same time', () => {
    renderPicker();

    expect(screen.getByRole('grid', { name: /choose a date/i })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /available times/i })).toBeInTheDocument();
  });

  it('keeps the calendar on screen after a date is picked', () => {
    // The whole reason the two steps were merged: changing your mind about the
    // day must not mean navigating backwards.
    renderPicker({ selectedDateKey: '2026-08-20' });

    expect(screen.getByRole('grid', { name: /choose a date/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^09:00/ })).toBeInTheDocument();
  });

  it('names the chosen day in the times heading', () => {
    renderPicker({ selectedDateKey: '2026-08-20' });

    const expected = new Date(2026, 7, 20).toLocaleDateString(undefined, {
      weekday: 'long', day: 'numeric', month: 'long', year: 'numeric',
    });
    expect(screen.getByRole('heading', { name: `Times on ${expected}` })).toBeInTheDocument();
  });
});

describe('SlotPicker — the calendar', () => {
  it('only offers days that actually have slots', async () => {
    const { user, onSelectDate } = renderPicker();

    const grid = screen.getByRole('grid', { name: /choose a date/i });
    const cells = within(grid).getAllByRole('gridcell');
    const enabled = cells.filter((c) => !c.hasAttribute('disabled'));

    expect(enabled).toHaveLength(1);
    await user.click(enabled[0]);
    expect(onSelectDate).toHaveBeenCalledWith('2026-08-20');
  });

  it('is a real grid, with rows', () => {
    // The markup used to declare `role="grid"` with gridcells as direct
    // children and no rows at all, which is not a grid.
    const grid = renderPicker().getByRole('grid', { name: /choose a date/i });

    expect(within(grid).getAllByRole('row')).toHaveLength(6);
  });

  it('marks the selected day as selected, not merely coloured', () => {
    renderPicker({ selectedDateKey: '2026-08-20' });

    const selected = screen.getAllByRole('gridcell').find((c) => c.getAttribute('aria-selected') === 'true');
    expect(selected).toHaveTextContent('20');
  });

  it('moves focus between available days with the arrow keys', async () => {
    const { user } = renderPicker({
      slots: [slot({ localDate: '2026-08-20' }), slot({ localDate: '2026-08-21' })],
    });

    // Derived from the same `toLocaleDateString` call the component makes: a
    // hardcoded "20 August 2026" fails on any machine with another locale, and
    // this one is not English.
    const dayLabel = (day: number) =>
      new Date(2026, 7, day).toLocaleDateString(undefined, {
        weekday: 'long', month: 'long', day: 'numeric', year: 'numeric',
      });

    screen.getByRole('gridcell', { name: dayLabel(20) }).focus();
    await user.keyboard('{ArrowRight}');

    expect(screen.getByRole('gridcell', { name: dayLabel(21) })).toHaveFocus();
  });

  it('will not walk backwards into months that can contain nothing', () => {
    // monthCursor is "this month", so Previous has nowhere legitimate to go.
    const thisMonth = new Date();
    thisMonth.setDate(1);
    renderPicker({ monthCursor: thisMonth });

    expect(screen.getByRole('button', { name: 'Previous month' })).toBeDisabled();
  });

  it('offers a day the backend offered even when the visitor’s own date is ahead of it', () => {
    // REGRESSION: the calendar also required `dateKey >= today`, where "today"
    // came from `new Date()` - the VISITOR's clock - while the slot keys are
    // organizer-local wall clock. A guest whose local date runs ahead of the
    // organizer's (Tokyo booking a Los Angeles organizer, at most hours of the
    // day) had the earliest bookable day rendered disabled while it still
    // showed the dot that says it is open, and could not book it at all.
    //
    // The backend already applies the same-day cutoff, the minimum notice and
    // the booking window against the organizer's own TimeZoneInfo, so a day it
    // sends is bookable by definition and the client must not re-decide.
    const now = new Date();
    const organizerYesterday = new Date(now);
    organizerYesterday.setDate(organizerYesterday.getDate() - 1);
    const key = [
      organizerYesterday.getFullYear(),
      String(organizerYesterday.getMonth() + 1).padStart(2, '0'),
      String(organizerYesterday.getDate()).padStart(2, '0'),
    ].join('-');

    renderPicker({
      monthCursor: new Date(organizerYesterday.getFullYear(), organizerYesterday.getMonth(), 1),
      slots: [slot({ localDate: key })],
    });

    const label = organizerYesterday.toLocaleDateString(undefined, {
      weekday: 'long', month: 'long', day: 'numeric', year: 'numeric',
    });
    expect(screen.getByRole('gridcell', { name: label })).toBeEnabled();
  });

  it('still refuses a day the backend did not offer', () => {
    // The other half of the same rule: dropping the date comparison must not
    // turn the calendar into "every square is clickable".
    renderPicker();

    const grid = screen.getByRole('grid', { name: /choose a date/i });
    const enabled = within(grid).getAllByRole('gridcell').filter((c) => !c.hasAttribute('disabled'));

    expect(enabled).toHaveLength(1);
  });
});

describe('SlotPicker — the times', () => {
  it('labels a slot with the organizer\'s wall clock, not the browser\'s', () => {
    // REGRESSION: the label used to be `new Date(startUtc).toLocaleTimeString()`
    // while the booking recorded `localStartTime`, so a visitor in another zone
    // clicked "04:00" and was told on the next screen they had booked "10:00".
    renderPicker({ selectedDateKey: '2026-08-20' });

    expect(screen.getByRole('button', { name: /^09:00/ })).toBeInTheDocument();
  });

  it('adds the visitor\'s own reading when the two zones differ', () => {
    renderPicker({ selectedDateKey: '2026-08-20', timeZoneId: 'Pacific/Kiritimati' });

    const expected = new Date('2026-08-20T07:00:00Z').toLocaleTimeString([], {
      hour: '2-digit', minute: '2-digit',
    });
    expect(screen.getByText(`${expected} your time`)).toBeInTheDocument();
  });

  it('says nothing about "your time" when the visitor is already in that zone', () => {
    const local = Intl.DateTimeFormat().resolvedOptions().timeZone;
    renderPicker({ selectedDateKey: '2026-08-20', timeZoneId: local });

    expect(screen.queryByText(/your time/)).not.toBeInTheDocument();
  });

  it('is a group of toggle buttons rather than a listbox it does not implement', () => {
    // REGRESSION: `role="listbox"`/`role="option"` with `aria-selected={false}`
    // hardcoded promised arrow-key navigation and a selected option, neither of
    // which existed. The same fake-ARIA problem, one screen over.
    renderPicker({ selectedDateKey: '2026-08-20' });

    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
    expect(screen.getByRole('group', { name: /available times/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^09:00/ })).toHaveAttribute('aria-pressed', 'false');
  });

  it('reports a chosen slot as pressed', () => {
    renderPicker({ selectedDateKey: '2026-08-20', selectedSlot: slot() });

    expect(screen.getByRole('button', { name: /^09:00/ })).toHaveAttribute('aria-pressed', 'true');
  });

  it('hands the whole slot back, so the caller keeps the real instant', async () => {
    const { user, onSelectSlot } = renderPicker({ selectedDateKey: '2026-08-20' });

    await user.click(screen.getByRole('button', { name: /^09:00/ }));

    expect(onSelectSlot).toHaveBeenCalledWith(expect.objectContaining({ startUtc: '2026-08-20T07:00:00Z' }));
  });
});

describe('SlotPicker — states', () => {
  it('says what to do next before a date is chosen', () => {
    renderPicker();

    expect(screen.getByText(/pick a date/i)).toBeInTheDocument();
  });

  it('explains an empty day and points back at the calendar', () => {
    renderPicker({ selectedDateKey: '2026-08-21' });

    expect(screen.getByText(/no times on this date/i)).toBeInTheDocument();
  });

  it('offers the next month when the whole month is empty', async () => {
    const { user, onMonthChange } = renderPicker({ slots: [] });

    await user.click(screen.getByRole('button', { name: /try the next month/i }));

    expect(onMonthChange).toHaveBeenCalledWith(new Date(2026, 8, 1));
  });

  it('keeps the layout while loading rather than blanking it', () => {
    renderPicker({ loading: true });

    // Both panels report themselves as loading; neither is replaced by the
    // word "Loading..." on an empty screen.
    expect(screen.getAllByRole('status', { name: 'Loading' })).toHaveLength(2);
    expect(screen.getByRole('heading', { name: /available times/i })).toBeInTheDocument();
  });
});

/**
 * A failed slot fetch used to be indistinguishable from an organizer with no
 * free time: `useAvailableSlots` had no `catch`, so `slots` stayed empty and
 * this rendered "Nothing available in <month>" with a "Try the next month"
 * button - sending a guest to look at a month that would fail identically.
 */
describe('SlotPicker — the times could not be loaded', () => {
  it('says so rather than reporting an empty month', () => {
    renderPicker({ error: 'We could not load the available times.' });

    expect(screen.getByRole('alert')).toHaveTextContent('We could not load the available times.');
    expect(screen.queryByText(/Nothing available in/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /try the next month/i })).not.toBeInTheDocument();
  });

  it('does not leave a calendar of unavailable days behind the message', () => {
    // A month drawn with no dots is the same wrong claim in the other panel.
    renderPicker({ error: 'We could not load the available times.' });

    expect(screen.queryByRole('row')).not.toBeInTheDocument();
  });

  it('offers a retry that re-runs the request', async () => {
    const onRetry = vi.fn();
    const { user } = renderPicker({ error: 'We could not load the available times.', onRetry });

    await user.click(screen.getByRole('button', { name: 'Try again' }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });
});
