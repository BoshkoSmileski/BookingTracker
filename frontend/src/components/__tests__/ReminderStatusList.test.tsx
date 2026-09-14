import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { ReminderStatusList } from '../ReminderStatusList';
import { StatusBadge } from '../StatusBadge';
import { reminder } from '../../test/factories';
import { renderWithProviders } from '../../test/render';

describe('ReminderStatusList', () => {
  it('renders an explanatory empty state rather than a blank panel', () => {
    renderWithProviders(<ReminderStatusList reminders={[]} />);

    // Says *why* there is nothing, which is the difference between a useful
    // empty state and a dead end.
    expect(screen.getByText(/no reminders for this booking/i)).toBeInTheDocument();
    expect(screen.getByText(/reminders are switched off|too close to its start time/i)).toBeInTheDocument();
  });

  it('shows the lead-time label and scheduled time for an upcoming reminder', () => {
    renderWithProviders(<ReminderStatusList reminders={[reminder({ label: '24 hours' })]} />);

    expect(screen.getByText('24 hours before')).toBeInTheDocument();
    expect(screen.getByText(/scheduled for/i)).toBeInTheDocument();
  });

  it('maps the scheduling status to organizer-facing wording', () => {
    // The DTO says "Scheduled"; an organizer reads "Upcoming".
    renderWithProviders(<ReminderStatusList reminders={[reminder({ status: 'Scheduled' })]} />);

    expect(screen.getByText('Upcoming')).toBeInTheDocument();
    expect(screen.queryByText('Scheduled')).not.toBeInTheDocument();
  });

  it.each([
    ['Sent', 'Sent'],
    ['Queued', 'Queued'],
    ['Failed', 'Failed'],
    ['Cancelled', 'Cancelled'],
    ['Skipped', 'Skipped'],
  ] as const)('renders the %s status', (status, label) => {
    renderWithProviders(<ReminderStatusList reminders={[reminder({ status })]} />);

    expect(screen.getByText(label)).toBeInTheDocument();
  });

  it('shows the sent time once a reminder has been delivered', () => {
    renderWithProviders(<ReminderStatusList reminders={[
      reminder({ status: 'Sent', queuedAtUtc: '2026-08-19T07:00:01', sentAtUtc: '2026-08-19T07:00:05' }),
    ]} />);

    // "Sent" legitimately appears twice - as the status pill and as the timestamp
    // label - so it is counted rather than matched as if it were unique.
    const item = screen.getByRole('listitem');
    expect(within(item).getAllByRole('term').map((t) => t.textContent)).toEqual(['Scheduled for', 'Sent']);
    expect(within(item).getAllByText('Sent')).toHaveLength(2);
    // Queued time is hidden once sent - showing both would be noise.
    expect(within(item).queryByText('Queued')).not.toBeInTheDocument();
  });

  it('shows the queued time while delivery is still pending', () => {
    renderWithProviders(<ReminderStatusList reminders={[
      reminder({ status: 'Queued', queuedAtUtc: '2026-08-19T07:00:01', sentAtUtc: null }),
    ]} />);

    expect(screen.getAllByText('Queued').length).toBeGreaterThan(0);
  });

  it('surfaces the delivery error and retry count on a failed reminder', () => {
    renderWithProviders(<ReminderStatusList reminders={[
      reminder({ status: 'Failed', attemptCount: 3, failureReason: 'SMTP connection refused' }),
    ]} />);

    expect(screen.getByText(/delivery error: smtp connection refused/i)).toBeInTheDocument();
    expect(screen.getByText('Retries')).toBeInTheDocument();
    expect(screen.getByText('3')).toBeInTheDocument();
  });

  it('hides the retry row for a clean first-attempt send', () => {
    // AttemptCount counts FAILED attempts, so 0 means "no retries needed".
    renderWithProviders(<ReminderStatusList reminders={[reminder({ status: 'Sent', attemptCount: 0 })]} />);

    expect(screen.queryByText('Retries')).not.toBeInTheDocument();
  });

  it('explains why a reminder was cancelled or skipped', () => {
    renderWithProviders(<ReminderStatusList reminders={[
      reminder({ status: 'Cancelled', resolutionReason: 'Booking rescheduled' }),
    ]} />);

    expect(screen.getByText('Booking rescheduled')).toBeInTheDocument();
  });

  it('renders one list item per reminder', () => {
    renderWithProviders(<ReminderStatusList reminders={[
      reminder({ id: 'a', label: '24 hours', minutesBeforeEvent: 1440 }),
      reminder({ id: 'b', label: '1 hour', minutesBeforeEvent: 60, status: 'Sent' }),
      reminder({ id: 'c', label: '15 minutes', minutesBeforeEvent: 15, status: 'Cancelled' }),
    ]} />);

    const items = screen.getAllByRole('listitem');
    expect(items).toHaveLength(3);
    expect(within(items[0]).getByText('24 hours before')).toBeInTheDocument();
    expect(within(items[2]).getByText('15 minutes before')).toBeInTheDocument();
  });
});

describe('StatusBadge', () => {
  it.each(['Active', 'Submitted', 'Abandoned', 'Cancelled'])('renders the %s status text', (status) => {
    renderWithProviders(<StatusBadge status={status} />);

    expect(screen.getByText(status)).toBeInTheDocument();
  });

  /**
   * The status marker is a coloured dot beside the label, hidden from assistive
   * tech because the label already carries the meaning. Comparing dots to each
   * other rather than to a literal Tailwind class keeps these about "are these
   * distinguishable" instead of about which shade of green is in use.
   */
  const dotClassOf = (container: HTMLElement) =>
    container.querySelector('span[aria-hidden="true"]')?.className ?? '';

  it('distinguishes known statuses from one another', () => {
    const { container, unmount } = renderWithProviders(<StatusBadge status="Submitted" />);
    const submitted = dotClassOf(container);
    unmount();

    const abandoned = renderWithProviders(<StatusBadge status="Abandoned" />);
    expect(dotClassOf(abandoned.container)).not.toBe(submitted);
  });

  it('distinguishes Cancelled from Abandoned', () => {
    // REGRESSION: Cancelled was missing from the colour map and fell through to
    // the same grey as Abandoned - two very different outcomes, identical mark.
    const { container, unmount } = renderWithProviders(<StatusBadge status="Cancelled" />);
    const cancelled = dotClassOf(container);
    unmount();

    const abandoned = renderWithProviders(<StatusBadge status="Abandoned" />);
    expect(dotClassOf(abandoned.container)).not.toBe(cancelled);
  });

  it('falls back to the neutral marker for an unknown status instead of rendering unmarked', () => {
    const { container, unmount } = renderWithProviders(<StatusBadge status="SomethingNew" />);
    expect(screen.getByText('SomethingNew')).toBeInTheDocument();
    const unknown = dotClassOf(container);
    expect(unknown).not.toBe('');
    unmount();

    const neutral = renderWithProviders(<StatusBadge status="Abandoned" />);
    expect(unknown).toBe(dotClassOf(neutral.container));
  });
});
