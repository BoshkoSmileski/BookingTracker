import { formatClock } from '../lib/dates';
import { describeEvent, formatEventValue, hasValueDiff } from '../lib/eventDisplay';
import type { BookingSessionEventDto } from '../lib/types';

interface EventTimelineProps {
  events: BookingSessionEventDto[];
}

/**
 * Renders the full, immutable interaction history exactly as it happened -
 * this is the "12:00:01 Booking page opened / 12:00:04 Name changed NULL -> J"
 * view. Sorted by ClientSequenceNumber (falling back to Id) rather than
 * insertion order, since live SignalR pushes can arrive slightly out of order.
 */
export function EventTimeline({ events }: EventTimelineProps) {
  const sorted = [...events].sort(
    (a, b) => a.clientSequenceNumber - b.clientSequenceNumber || a.id - b.id,
  );

  if (sorted.length === 0) {
    return <p className="text-sm text-gray-500">No events recorded yet.</p>;
  }

  return (
    <ol className="space-y-3">
      {sorted.map((e) => (
        <li key={e.id} className="flex gap-3 text-sm">
          {/* The time is what you scan a timeline by, so it is not the faintest
              thing on it — it was gray-400, one step below the description it
              indexes, which inverted the hierarchy of the whole list. */}
          <span className="w-20 shrink-0 tabular-nums text-gray-500">{formatClock(e.timestamp)}</span>
          {/*
            `min-w-0` + `break-words`, because the values here are whatever a
            guest typed. A tracked `Message` containing a URL has no break
            opportunity in it, so without `min-w-0` this flex item could not
            shrink below that one unbreakable word and the whole page scrolled
            sideways - measured at 124px of overflow on a 375px viewport. Same
            pair the Answers list above already uses, and for the same reason.
          */}
          <div className="min-w-0">
            <p className="font-medium break-words text-gray-900">{describeEvent(e)}</p>
            {hasValueDiff(e) && (
              <p className="break-words text-gray-500">
                <span className="font-mono text-xs">{formatEventValue(e, e.oldValue)}</span>
                {' → '}
                <span className="font-mono text-xs">{formatEventValue(e, e.newValue)}</span>
              </p>
            )}
          </div>
        </li>
      ))}
    </ol>
  );
}
