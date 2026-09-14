import { useId, useState } from 'react';
import { INK, SEQUENTIAL, SERIES, STATUS_COLOR, sequentialStep } from './chartTheme';
import type { HourCountDto, StatusSliceDto, TrendPointDto, WeekdayCountDto } from '../../lib/types';

/**
 * Hand-rolled SVG charts. No charting dependency: the forms needed here (one
 * line, one doughnut, horizontal bars, a funnel) are simple enough that a
 * library would add far more bundle and API surface than it saves, and the
 * project deliberately runs with a minimal dependency set.
 *
 * Shared rules applied throughout: 2px lines, >=8px hover targets, a 2px surface
 * gap between adjacent fills, recessive grid/axis ink, and values shown as text
 * (never colour alone) so the palette's sub-3:1 slots stay readable.
 */

const TOOLTIP_CLASS =
  'pointer-events-none absolute z-10 -translate-x-1/2 -translate-y-full rounded-md bg-gray-900 px-2 py-1 text-xs text-white shadow';

/** Sessions vs bookings over time. Two series -> legend is mandatory. */
export function TrendChart({ points }: { points: TrendPointDto[] }) {
  const [hover, setHover] = useState<number | null>(null);
  const gradientId = useId();

  const width = 720;
  const height = 220;
  const pad = { top: 12, right: 12, bottom: 26, left: 34 };
  const plotW = width - pad.left - pad.right;
  const plotH = height - pad.top - pad.bottom;

  const max = Math.max(1, ...points.map((p) => Math.max(p.sessions, p.bookings)));
  const stepX = points.length > 1 ? plotW / (points.length - 1) : 0;
  const x = (i: number) => pad.left + i * stepX;
  const y = (v: number) => pad.top + plotH - (v / max) * plotH;

  const path = (key: 'sessions' | 'bookings') =>
    points.map((p, i) => `${i === 0 ? 'M' : 'L'} ${x(i).toFixed(1)} ${y(p[key]).toFixed(1)}`).join(' ');

  const ticks = [0, Math.round(max / 2), max].filter((v, i, a) => a.indexOf(v) === i);
  // At most ~6 date labels so they never collide, however long the range is.
  const labelEvery = Math.max(1, Math.ceil(points.length / 6));
  const active = hover !== null ? points[hover] : null;

  return (
    <div className="relative">
      <div className="mb-3 flex flex-wrap gap-4 text-xs">
        <LegendSwatch color={SERIES[0]} label="Sessions started" />
        <LegendSwatch color={SERIES[2]} label="Bookings confirmed" />
      </div>

      <svg viewBox={`0 0 ${width} ${height}`} className="w-full" role="img" aria-label="Sessions and bookings over time">
        <defs>
          <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor={SERIES[0]} stopOpacity="0.16" />
            <stop offset="100%" stopColor={SERIES[0]} stopOpacity="0" />
          </linearGradient>
        </defs>

        {ticks.map((t) => (
          <g key={t}>
            <line x1={pad.left} x2={width - pad.right} y1={y(t)} y2={y(t)} stroke={INK.grid} strokeWidth="1" />
            <text x={pad.left - 6} y={y(t) + 3} textAnchor="end" fontSize="10" fill={INK.muted}>{t}</text>
          </g>
        ))}

        <path d={`${path('sessions')} L ${x(points.length - 1)} ${pad.top + plotH} L ${x(0)} ${pad.top + plotH} Z`} fill={`url(#${gradientId})`} />
        <path d={path('sessions')} fill="none" stroke={SERIES[0]} strokeWidth="2" strokeLinejoin="round" strokeLinecap="round" />
        <path d={path('bookings')} fill="none" stroke={SERIES[2]} strokeWidth="2" strokeLinejoin="round" strokeLinecap="round" />

        {points.map((p, i) => (
          <g key={p.date}>
            {i % labelEvery === 0 && (
              <text x={x(i)} y={height - 8} textAnchor="middle" fontSize="10" fill={INK.muted}>
                {p.date.slice(5)}
              </text>
            )}
            {hover === i && (
              <>
                <line x1={x(i)} x2={x(i)} y1={pad.top} y2={pad.top + plotH} stroke={INK.axis} strokeWidth="1" />
                <circle cx={x(i)} cy={y(p.sessions)} r="4" fill={SERIES[0]} stroke={INK.surface} strokeWidth="2" />
                <circle cx={x(i)} cy={y(p.bookings)} r="4" fill={SERIES[2]} stroke={INK.surface} strokeWidth="2" />
              </>
            )}
            {/* Hit target far wider than the mark, per the interaction rules. */}
            <rect
              x={x(i) - Math.max(6, stepX / 2)}
              y={pad.top}
              width={Math.max(12, stepX)}
              height={plotH}
              fill="transparent"
              onMouseEnter={() => setHover(i)}
              onMouseLeave={() => setHover(null)}
            />
          </g>
        ))}
      </svg>

      {active && (
        <div className={TOOLTIP_CLASS} style={{ left: `${(x(hover!) / width) * 100}%`, top: 20 }}>
          <div className="font-medium">{active.date}</div>
          <div>{active.sessions} sessions</div>
          <div>{active.bookings} bookings</div>
        </div>
      )}
    </div>
  );
}

/** Mutually exclusive booking states. Legend carries counts + shares, which is also the contrast relief. */
export function StatusDoughnut({ slices }: { slices: StatusSliceDto[] }) {
  const total = slices.reduce((sum, s) => sum + s.count, 0);
  const size = 168;
  const stroke = 26;
  const radius = (size - stroke) / 2;
  const circumference = 2 * Math.PI * radius;

  let offset = 0;

  return (
    <div className="flex flex-col items-center gap-5 sm:flex-row sm:items-center">
      <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} role="img" aria-label="Booking status distribution">
        <g transform={`rotate(-90 ${size / 2} ${size / 2})`}>
          {slices.map((slice) => {
            const fraction = total === 0 ? 0 : slice.count / total;
            // 2px surface gap between adjacent segments.
            const dash = Math.max(0, fraction * circumference - 2);
            const el = (
              <circle
                key={slice.status}
                cx={size / 2}
                cy={size / 2}
                r={radius}
                fill="none"
                stroke={STATUS_COLOR[slice.status] ?? SERIES[0]}
                strokeWidth={stroke}
                strokeDasharray={`${dash} ${circumference - dash}`}
                strokeDashoffset={-offset}
              />
            );
            offset += fraction * circumference;
            return el;
          })}
        </g>
        <text x={size / 2} y={size / 2 - 2} textAnchor="middle" fontSize="22" fontWeight="600" fill={INK.primary}>{total}</text>
        <text x={size / 2} y={size / 2 + 14} textAnchor="middle" fontSize="10" fill={INK.muted}>sessions</text>
      </svg>

      <ul className="w-full space-y-1.5">
        {slices.map((slice) => (
          <li key={slice.status} className="flex items-center justify-between gap-3 text-sm">
            <span className="flex items-center gap-2 text-gray-700">
              <span className="h-2.5 w-2.5 shrink-0 rounded-full" style={{ background: STATUS_COLOR[slice.status] ?? SERIES[0] }} />
              {slice.status}
            </span>
            <span className="tabular-nums text-gray-500">
              <span className="font-medium text-gray-900">{slice.count}</span> · {(slice.share * 100).toFixed(0)}%
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}

/** Horizontal magnitude bars, one hue, more-is-darker. Values direct-labelled. */
export function BarList({
  rows,
  emptyLabel,
}: {
  rows: { key: string; label: string; value: number; caption?: string }[];
  emptyLabel?: string;
}) {
  const max = Math.max(0, ...rows.map((r) => r.value));
  if (max === 0) {
    return <p className="text-sm text-gray-500">{emptyLabel ?? 'No data in this range.'}</p>;
  }

  return (
    <ul className="space-y-2">
      {rows.map((row) => (
        <li key={row.key} className="grid grid-cols-[5.5rem_1fr_auto] items-center gap-3 text-sm">
          <span className="truncate text-gray-600">{row.label}</span>
          <span className="h-5 overflow-hidden rounded bg-gray-50">
            <span
              className="block h-full rounded"
              style={{ width: `${(row.value / max) * 100}%`, background: sequentialStep(row.value, max), minWidth: row.value > 0 ? 4 : 0 }}
            />
          </span>
          <span className="tabular-nums text-xs text-gray-500">{row.caption ?? row.value}</span>
        </li>
      ))}
    </ul>
  );
}

export function WeekdayChart({ data }: { data: WeekdayCountDto[] }) {
  return (
    <BarList
      rows={data.map((d) => ({ key: String(d.dayOfWeek), label: d.label.slice(0, 3), value: d.count }))}
      emptyLabel="No bookings yet - weekday patterns appear once bookings come in."
    />
  );
}

export function HourChart({ data, timeZoneId }: { data: HourCountDto[]; timeZoneId: string }) {
  return (
    <>
      <BarList
        rows={data.map((d) => ({ key: String(d.hour), label: `${String(d.hour).padStart(2, '0')}:00`, value: d.count }))}
        emptyLabel="No bookings yet - popular hours appear once bookings come in."
      />
      <p className="mt-3 text-xs text-gray-500">Times shown in {timeZoneId}.</p>
    </>
  );
}

/** Ordered funnel stages: an ordinal ramp, each stage labelled with count, share of entry, and step conversion. */
export function FunnelChart({ steps }: { steps: { step: string; count: number; shareOfEntry: number; stepConversion: number }[] }) {
  const entry = steps[0]?.count ?? 0;
  if (entry === 0) {
    return <p className="text-sm text-gray-500">No visitors in this range yet.</p>;
  }

  return (
    <ol className="space-y-2">
      {steps.map((step, i) => {
        const width = Math.max(4, step.shareOfEntry * 100);
        const dropped = i > 0 ? steps[i - 1].count - step.count : 0;
        return (
          <li key={step.step}>
            <div className="flex items-baseline justify-between gap-2 text-sm">
              <span className="text-gray-700">{step.step}</span>
              <span className="tabular-nums text-gray-500">
                <span className="font-medium text-gray-900">{step.count}</span> · {(step.shareOfEntry * 100).toFixed(0)}%
              </span>
            </div>
            <div className="mt-1 h-6 overflow-hidden rounded bg-gray-50">
              <div
                className="flex h-full items-center rounded px-2"
                style={{ width: `${width}%`, background: SEQUENTIAL[Math.min(i, SEQUENTIAL.length - 1)] }}
              />
            </div>
            {i > 0 && (
              <p className="mt-0.5 text-xs text-gray-500">
                {(step.stepConversion * 100).toFixed(0)}% continued
                {dropped > 0 && <> · {dropped} dropped off</>}
              </p>
            )}
          </li>
        );
      })}
    </ol>
  );
}

function LegendSwatch({ color, label }: { color: string; label: string }) {
  return (
    <span className="flex items-center gap-1.5 text-gray-600">
      <span className="h-2.5 w-2.5 rounded-full" style={{ background: color }} />
      {label}
    </span>
  );
}
