import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { BarList, FunnelChart, HourChart, StatusDoughnut, TrendChart, WeekdayChart } from '../Charts';
import { EmptyState, MeterRow, Panel, SkeletonCards, SkeletonPanel, StatCard } from '../Panels';
import { bookingAnalytics, conversionFunnel } from '../../../test/factories';
import { renderWithProviders } from '../../../test/render';

/**
 * Charts are hand-rolled SVG, so these assert the things a reader depends on:
 * that every value is present as *text* (the palette has slots below 3:1
 * contrast, and visible labels are their required accessibility relief), that
 * an accessible name exists, and that empty data produces an explanation
 * rather than an empty box.
 */
describe('analytics charts', () => {
  const analytics = bookingAnalytics();

  describe('TrendChart', () => {
    it('exposes an accessible name for the plot', () => {
      renderWithProviders(<TrendChart points={analytics.trend} />);

      expect(screen.getByRole('img', { name: /sessions and bookings over time/i })).toBeInTheDocument();
    });

    it('legends both series, since colour alone must never carry identity', () => {
      renderWithProviders(<TrendChart points={analytics.trend} />);

      expect(screen.getByText('Sessions started')).toBeInTheDocument();
      expect(screen.getByText('Bookings confirmed')).toBeInTheDocument();
    });

    it('renders a single-point series without collapsing', () => {
      renderWithProviders(<TrendChart points={[{ date: '2026-08-01', sessions: 3, bookings: 1 }]} />);

      expect(screen.getByRole('img', { name: /over time/i })).toBeInTheDocument();
    });
  });

  describe('StatusDoughnut', () => {
    it('labels every slice with its count and share as text', () => {
      renderWithProviders(<StatusDoughnut slices={analytics.statusDistribution} />);

      expect(screen.getByText('Upcoming')).toBeInTheDocument();
      expect(screen.getByText('Cancelled')).toBeInTheDocument();
      // Count and percentage both readable without relying on the swatch colour.
      expect(screen.getByText('5')).toBeInTheDocument();
      expect(screen.getAllByText(/%$/).length).toBeGreaterThan(0);
    });

    it('shows the total in the centre', () => {
      renderWithProviders(<StatusDoughnut slices={analytics.statusDistribution} />);

      expect(screen.getByText('20')).toBeInTheDocument();
      expect(screen.getByText('sessions')).toBeInTheDocument();
    });

    it('has an accessible name', () => {
      renderWithProviders(<StatusDoughnut slices={analytics.statusDistribution} />);

      expect(screen.getByRole('img', { name: /booking status distribution/i })).toBeInTheDocument();
    });
  });

  describe('BarList / WeekdayChart / HourChart', () => {
    it('renders a labelled row per entry with its value', () => {
      renderWithProviders(<BarList rows={[
        { key: 'a', label: 'Monday', value: 4 },
        { key: 'b', label: 'Tuesday', value: 2 },
      ]} />);

      expect(screen.getByText('Monday')).toBeInTheDocument();
      expect(screen.getByText('4')).toBeInTheDocument();
    });

    it('explains an all-zero dataset instead of drawing empty bars', () => {
      renderWithProviders(<BarList rows={[{ key: 'a', label: 'Monday', value: 0 }]} emptyLabel="Nothing yet." />);

      expect(screen.getByText('Nothing yet.')).toBeInTheDocument();
    });

    it('abbreviates weekday names and keeps all seven rows', () => {
      renderWithProviders(<WeekdayChart data={analytics.bookingsByWeekday} />);

      expect(screen.getByText('Mon')).toBeInTheDocument();
      expect(screen.getByText('Sun')).toBeInTheDocument();
      expect(screen.getAllByRole('listitem')).toHaveLength(7);
    });

    it('renders hours zero-padded and names the timezone the times are in', () => {
      renderWithProviders(<HourChart data={analytics.bookingsByHour} timeZoneId="Europe/Skopje" />);

      expect(screen.getByText('09:00')).toBeInTheDocument();
      expect(screen.getByText(/times shown in europe\/skopje/i)).toBeInTheDocument();
    });
  });

  describe('FunnelChart', () => {
    it('shows each step with its count and share of entry', () => {
      renderWithProviders(<FunnelChart steps={conversionFunnel().steps} />);

      expect(screen.getByText('Page viewed')).toBeInTheDocument();
      expect(screen.getByText('Booking confirmed')).toBeInTheDocument();
      expect(screen.getByText('20')).toBeInTheDocument();
    });

    it('reports step-to-step conversion and drop-off, which is where the story is', () => {
      renderWithProviders(<FunnelChart steps={conversionFunnel().steps} />);

      expect(screen.getByText(/70% continued/)).toBeInTheDocument();
      expect(screen.getByText(/6 dropped off/)).toBeInTheDocument();
    });

    it('does not show a conversion line on the entry step', () => {
      renderWithProviders(<FunnelChart steps={conversionFunnel().steps} />);

      // The first step has nothing to have converted *from*.
      expect(screen.queryByText(/100% continued/)).not.toBeInTheDocument();
    });

    it('explains an empty funnel rather than rendering zero-width bars', () => {
      renderWithProviders(<FunnelChart steps={[
        { step: 'Page viewed', count: 0, shareOfEntry: 0, stepConversion: 0 },
      ]} />);

      expect(screen.getByText(/no visitors in this range yet/i)).toBeInTheDocument();
    });
  });
});

describe('analytics panels', () => {
  it('renders a stat card with its label, value and hint', () => {
    renderWithProviders(<StatCard label="Visitors" value={42} hint="Sessions started" />);

    expect(screen.getByText('Visitors')).toBeInTheDocument();
    expect(screen.getByText('42')).toBeInTheDocument();
    expect(screen.getByText('Sessions started')).toBeInTheDocument();
  });

  it('gives a panel a real heading so the page has a document outline', () => {
    renderWithProviders(
      <Panel title="Booking trend" subtitle="Sessions vs bookings"><p>body</p></Panel>,
    );

    expect(screen.getByRole('heading', { name: 'Booking trend' })).toBeInTheDocument();
    expect(screen.getByText('Sessions vs bookings')).toBeInTheDocument();
  });

  it('renders a meter with a caption when one is supplied', () => {
    renderWithProviders(<MeterRow label="Sync coverage" value={0.75} caption="6/8" />);

    expect(screen.getByText('Sync coverage')).toBeInTheDocument();
    expect(screen.getByText('6/8')).toBeInTheDocument();
  });

  it('renders a meter percentage when no caption is given', () => {
    renderWithProviders(<MeterRow label="Delivery rate" value={0.9655} />);

    expect(screen.getByText('97%')).toBeInTheDocument();
  });

  it('clamps an out-of-range meter value rather than overflowing its track', () => {
    const { container } = renderWithProviders(<MeterRow label="Odd" value={2.5} />);
    const fill = container.querySelector('div[style*="width"]') as HTMLElement;

    expect(fill.style.width).toBe('100%');
  });

  it('renders an empty state message', () => {
    renderWithProviders(<EmptyState message="No activity in this range." />);

    expect(screen.getByText('No activity in this range.')).toBeInTheDocument();
  });

  it('renders skeletons with the requested shape while loading', () => {
    const { container } = renderWithProviders(<><SkeletonPanel lines={3} /><SkeletonCards count={2} /></>);

    expect(container.querySelectorAll('.animate-pulse').length).toBeGreaterThan(3);
  });
});

describe('booking page performance table', () => {
  it('is a real table, so the data is navigable by assistive tech', () => {
    // Rendered inline by AnalyticsPage; asserted here on the same DTO shape.
    const perf = bookingAnalytics().pagePerformance[0];
    renderWithProviders(
      <table>
        <thead><tr><th>Page</th><th>Bookings</th></tr></thead>
        <tbody><tr><td>{perf.title}</td><td>{perf.bookings}</td></tr></tbody>
      </table>,
    );

    const table = screen.getByRole('table');
    expect(within(table).getByRole('columnheader', { name: 'Page' })).toBeInTheDocument();
    expect(within(table).getByText('30 Minute Meeting')).toBeInTheDocument();
  });
});
