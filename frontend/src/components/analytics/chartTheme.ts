/**
 * Chart palette and shared geometry.
 *
 * These hexes are the validated categorical slots (slot order is the
 * colorblind-safety mechanism, not decoration - it must not be reordered or
 * cycled). Validated against this app's white card surface: lightness band,
 * chroma floor, adjacent-pair CVD separation and normal-vision floor all pass;
 * aqua/yellow/magenta fall below 3:1 contrast, which is why every chart using
 * them ships visible labels or a legend with values rather than relying on the
 * swatch alone.
 *
 * The app is light-mode only (no dark styles anywhere in the UI), so a single
 * set of values is correct here - a dark set would be selected separately, never
 * an automatic flip.
 */

/** Fixed categorical order. Assign by index; never generate a 9th hue. */
export const SERIES = ['#2a78d6', '#eb6834', '#1baf7a', '#eda100', '#e87ba4'] as const;

/** Single-hue ramp for magnitude (bars, funnel). Light -> dark = low -> high. */
export const SEQUENTIAL = ['#86b6ef', '#5598e7', '#3987e5', '#2a78d6', '#256abf', '#184f95'] as const;

export const INK = {
  primary: '#0b0b0b',
  secondary: '#52514e',
  muted: '#898781',
  grid: '#e1e0d9',
  axis: '#c3c2b7',
  surface: '#ffffff',
} as const;

/** Mutually exclusive booking states -> fixed slot, so a status keeps its colour when filters change the series count. */
export const STATUS_COLOR: Record<string, string> = {
  Upcoming: SERIES[0],
  Completed: SERIES[2],
  Cancelled: SERIES[1],
  Abandoned: SERIES[4],
  'In progress': SERIES[3],
};

/** Picks a ramp step by rank so the tallest bar is darkest - magnitude read twice (length + depth). */
export function sequentialStep(value: number, max: number): string {
  if (max <= 0) return SEQUENTIAL[0];
  const index = Math.round((value / max) * (SEQUENTIAL.length - 1));
  return SEQUENTIAL[Math.max(0, Math.min(SEQUENTIAL.length - 1, index))];
}

export function formatPercent(value: number | null | undefined, digits = 0): string {
  if (value === null || value === undefined) return '—';
  return `${(value * 100).toFixed(digits)}%`;
}

/** Compact human duration: 45s, 3m 12s, 1h 4m. */
export function formatDuration(seconds: number | null | undefined): string {
  if (seconds === null || seconds === undefined) return '—';
  const total = Math.max(0, Math.round(seconds));
  if (total < 60) return `${total}s`;
  const minutes = Math.floor(total / 60);
  if (minutes < 60) return `${minutes}m ${total % 60}s`;
  return `${Math.floor(minutes / 60)}h ${minutes % 60}m`;
}
