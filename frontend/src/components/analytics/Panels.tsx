import type { ReactNode } from 'react';
import { CARD, EmptyState as SharedEmptyState } from '../ui';

/**
 * Re-exported rather than defined here: this file used to carry its own StatCard
 * with a different radius and label colour from the one the booking page
 * dashboard renders, so the same figure looked like two components. There is now
 * one, and analytics keeps importing it from where it always did.
 */
export { StatCard } from '../StatCard';

/**
 * Card shell every analytics panel sits in - one place for padding, border, and
 * heading rhythm.
 *
 * `min-w-0` is load-bearing rather than defensive. Every panel is a grid item,
 * and a single-column `grid` track is sized `auto`, whose *minimum* is the
 * item's content-based minimum size - so one long, unbreakable line anywhere
 * inside a panel widened the whole track. `truncate` does not prevent that:
 * `white-space: nowrap` makes an element's min-content its full string, and the
 * `min-w-0` further down (on the activity feed's flex item) only governs
 * shrinking within an already-definite width, not the intrinsic size the track
 * is derived from.
 *
 * Measured at 375px: one activity row reading `Booking page "..." created` had a
 * min-content of 395px, which took the track to 437px and gave the whole
 * analytics screen 86px of horizontal scroll. An explicit `min-width: 0` on the
 * grid item replaces that automatic minimum with zero, so the track takes the
 * width available and the truncation inside it starts working.
 *
 * It belongs here rather than at the six call sites because every one of them is
 * a grid item, and the next panel added would otherwise reintroduce it.
 */
export function Panel({
  title,
  subtitle,
  action,
  children,
  className = '',
}: {
  title: string;
  subtitle?: string;
  action?: ReactNode;
  children: ReactNode;
  className?: string;
}) {
  return (
    <section className={`${CARD} min-w-0 p-5 ${className}`}>
      <div className="mb-5 flex items-start justify-between gap-3">
        <div>
          <h2 className="text-base font-semibold tracking-tight text-gray-900">{title}</h2>
          {subtitle && <p className="mt-0.5 text-[13px] text-gray-500">{subtitle}</p>}
        </div>
        {action}
      </div>
      {children}
    </section>
  );
}

/**
 * Thin adapter over the app's one EmptyState. This file used to carry a second,
 * differently-styled implementation under the same name, so an empty analytics
 * panel and an empty list elsewhere looked like different products.
 */
export function EmptyState({ message }: { message: string }) {
  return <SharedEmptyState title={message} compact />;
}

/** Loading skeleton - same footprint as the loaded panel so the layout doesn't jump. */
export function SkeletonPanel({ lines = 4 }: { lines?: number }) {
  return (
    <div className={`${CARD} p-5`} role="status" aria-label="Loading">
      <div className="mb-5 h-4 w-40 animate-pulse rounded bg-gray-200" />
      <div className="space-y-3">
        {Array.from({ length: lines }).map((_, i) => (
          <div key={i} className="h-4 animate-pulse rounded bg-gray-100" style={{ width: `${90 - i * 12}%` }} />
        ))}
      </div>
    </div>
  );
}

export function SkeletonCards({ count = 4 }: { count?: number }) {
  return (
    <>
      {Array.from({ length: count }).map((_, i) => (
        <div key={i} className={`${CARD} px-4 py-3.5`} role="status" aria-label="Loading">
          <div className="h-3.5 w-24 animate-pulse rounded bg-gray-200" />
          <div className="mt-2.5 h-7 w-14 animate-pulse rounded bg-gray-100" />
        </div>
      ))}
    </>
  );
}

/** Small labelled ratio bar. Used for delivery/conversion rates where a full chart would be overkill. */
export function MeterRow({ label, value, caption }: { label: string; value: number; caption?: string }) {
  const pct = Math.max(0, Math.min(1, value)) * 100;
  return (
    <div>
      <div className="flex items-baseline justify-between gap-2 text-sm">
        <span className="text-gray-700">{label}</span>
        <span className="font-semibold tabular-nums text-gray-900">{caption ?? `${pct.toFixed(0)}%`}</span>
      </div>
      <div className="mt-1.5 h-2 overflow-hidden rounded-full bg-gray-100">
        <div className="h-full rounded-full bg-accent-500" style={{ width: `${pct}%` }} />
      </div>
    </div>
  );
}
