/**
 * Backend timestamps round-trip through SQL Server as DateTime with Kind
 * "Unspecified", so System.Text.Json serializes them without a trailing 'Z'
 * even though they're always UTC (set via DateTime.UtcNow). Treat any
 * offset-less ISO string as UTC rather than letting the browser assume local time.
 */
function parseUtc(iso: string): Date {
  const hasOffset = /Z$|[+-]\d{2}:\d{2}$/.test(iso);
  return new Date(hasOffset ? iso : `${iso}Z`);
}

export function formatClock(iso: string): string {
  return parseUtc(iso).toLocaleTimeString([], { hour12: false });
}

/** Full local date + time (e.g. for "last synced at", email-sent timestamps) - always parses through parseUtc first. */
export function formatDateTime(iso: string): string {
  return parseUtc(iso).toLocaleString();
}

export function formatRelative(iso: string): string {
  const diffSec = Math.max(0, Math.round((Date.now() - parseUtc(iso).getTime()) / 1000));
  if (diffSec < 60) return `${diffSec}s ago`;
  const diffMin = Math.round(diffSec / 60);
  if (diffMin < 60) return `${diffMin}m ago`;
  const diffHr = Math.round(diffMin / 60);
  if (diffHr < 24) return `${diffHr}h ago`;
  return `${Math.round(diffHr / 24)}d ago`;
}
