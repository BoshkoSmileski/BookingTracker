import { StatusPill } from './ui';
import type { StatusTone } from './ui';

/**
 * A booking session's status.
 *
 * `Cancelled` used to be missing from the colour map and fell through to the
 * same grey as `Abandoned` - two outcomes that mean very different things.
 *
 * Renders through the shared `StatusPill`, so this, reminder status, calendar
 * health and a booking page's enabled state all look like one idea. They were
 * four independent filled-pill implementations before.
 */
const TONES: Record<string, StatusTone> = {
  Active: 'info',
  Submitted: 'success',
  Cancelled: 'danger',
  Abandoned: 'neutral',
};

export function StatusBadge({ status, detail }: { status: string; detail?: string }) {
  return (
    <StatusPill tone={TONES[status] ?? 'neutral'} detail={detail}>
      {status}
    </StatusPill>
  );
}
