import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { DateCalendar } from '../DateCalendar';
import { renderWithProviders } from '../../../test/render';

/**
 * The weekday header row.
 *
 * It was the one place in the app that printed a weekday without asking the
 * platform - a hardcoded English `Mo Tu We Th Fr Sa Su` beside a month name and
 * a date list that both go through `toLocale*`. These pin the two properties
 * that matter: the labels come from the locale, and they still describe the
 * columns the grid actually draws.
 */
describe('DateCalendar weekday headers', () => {
  const renderCalendar = () =>
    renderWithProviders(
      <DateCalendar
        monthCursor={new Date(2026, 7, 1)}
        onMonthChange={() => {}}
        slots={[]}
        loading={false}
        selectedDateKey={null}
        onSelectDate={() => {}}
      />,
    );

  /** The header row is the one `aria-hidden` grid of seven cells above the weeks. */
  const headerLabels = (container: HTMLElement) =>
    Array.from(container.querySelectorAll('[aria-hidden="true"].grid-cols-7 > div')).map(
      (el) => el.textContent ?? '',
    );

  it('renders seven Monday-first labels taken from the platform locale', () => {
    const { container } = renderCalendar();
    const labels = headerLabels(container);

    expect(labels).toHaveLength(7);

    // Derived from the same API the component uses, never hardcoded - a fixed
    // "Mo" fails on any machine with another locale, which is the whole reason
    // this changed. 2024-01-01 was a Monday.
    const expected = Array.from({ length: 7 }, (_, i) =>
      new Intl.DateTimeFormat(undefined, { weekday: 'short', timeZone: 'UTC' })
        .format(new Date(Date.UTC(2024, 0, 1) + i * 86_400_000))
        .slice(0, 2),
    );
    expect(labels).toEqual(expected);
  });

  it('keeps the two-letter presentation', () => {
    const { container } = renderCalendar();
    for (const label of headerLabels(container)) {
      expect(label.length).toBeLessThanOrEqual(2);
      expect(label.length).toBeGreaterThan(0);
    }
  });

  it('leaves the headers out of the accessibility tree, because each day already names itself', () => {
    // Announcing a two-letter abbreviation as a column header would repeat, in
    // a less intelligible form, what every date button's accessible name says.
    const { container } = renderCalendar();
    const header = container.querySelector('.grid-cols-7[aria-hidden="true"]');
    expect(header).not.toBeNull();
    expect(screen.getByRole('grid', { name: 'Choose a date' })).toBeInTheDocument();
  });

  it('still draws seven columns of real grid rows', () => {
    // The headers must keep describing the grid: six weeks of seven cells.
    const { container } = renderCalendar();
    expect(screen.getAllByRole('row')).toHaveLength(6);
    expect(headerLabels(container)).toHaveLength(7);
  });
});
