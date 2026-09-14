export function toDateKey(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

/** Monday-first calendar grid for the given month, including leading/trailing days from adjacent months. */
export function buildMonthGrid(monthCursor: Date): Date[] {
  const firstOfMonth = new Date(monthCursor.getFullYear(), monthCursor.getMonth(), 1);
  const startOffset = (firstOfMonth.getDay() + 6) % 7; // Monday = 0
  const gridStart = new Date(firstOfMonth);
  gridStart.setDate(gridStart.getDate() - startOffset);

  return Array.from({ length: 42 }, (_, i) => {
    const d = new Date(gridStart);
    d.setDate(d.getDate() + i);
    return d;
  });
}

export function startOfMonthKey(monthCursor: Date): string {
  return toDateKey(new Date(monthCursor.getFullYear(), monthCursor.getMonth(), 1));
}

export function endOfMonthKey(monthCursor: Date): string {
  return toDateKey(new Date(monthCursor.getFullYear(), monthCursor.getMonth() + 1, 0));
}
