import { CARD } from './ui';

/**
 * A headline number.
 *
 * One implementation - the analytics dashboard used to carry a second one with a
 * different radius and label treatment, so the same figure looked like two
 * components one click apart. `analytics/Panels.tsx` re-exports this.
 *
 * Deliberately quieter than it was: 20px rather than 24px, and tighter padding.
 * Ten of these sit in a row on the analytics dashboard, and at that count an
 * oversized numeral stops being emphasis and becomes noise - nothing is
 * emphasised when everything is.
 */
export function StatCard({
  label,
  value,
  hint,
  tone = 'default',
}: {
  label: string;
  value: string | number;
  hint?: string;
  tone?: 'default' | 'good' | 'warning' | 'critical';
}) {
  const toneClass =
    tone === 'good' ? 'text-accent-700'
      : tone === 'warning' ? 'text-amber-700'
        : tone === 'critical' ? 'text-red-700'
          : 'text-gray-900';

  return (
    <div className={`${CARD} px-4 py-3.5`}>
      <p className="truncate text-[13px] font-medium text-gray-500" title={label}>{label}</p>
      <p className={`mt-1 text-[26px] font-semibold leading-none tabular-nums tracking-tight ${toneClass}`}>{value}</p>
      {hint && <p className="mt-1.5 truncate text-[13px] text-gray-500" title={hint}>{hint}</p>}
    </div>
  );
}
