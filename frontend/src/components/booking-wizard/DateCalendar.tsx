import { ChevronLeft, ChevronRight } from 'lucide-react';
import { useMemo, useRef, useState } from 'react';
import { buildMonthGrid, toDateKey } from '../../lib/calendarGrid';
import type { AvailableSlotDto } from '../../lib/types';
import { FOCUS_RING } from '../ui';

interface DateCalendarProps {
  monthCursor: Date;
  onMonthChange: (next: Date) => void;
  slots: AvailableSlotDto[];
  loading: boolean;
  selectedDateKey: string | null;
  onSelectDate: (dateKey: string) => void;
}

/**
 * Monday-first weekday initials, in the viewer's own locale.
 *
 * These were the hardcoded English `Mo Tu We Th Fr Sa Su`, which is the one
 * place in the app that printed a weekday without asking the platform - every
 * other date in the guest flow goes through `toLocale*` (see `calendarDates.ts`).
 *
 * Built from a **fixed reference week in UTC** - 2024-01-01 was a Monday - and
 * formatted with `timeZone: 'UTC'`, so this is a label lookup and not date
 * arithmetic: no local offset can shift which weekday a cell is named after.
 * The calendar's own dates, its selectable-day logic and the timezone contract
 * are untouched.
 *
 * Sliced to two characters to keep the existing presentation. `weekday: 'narrow'`
 * would have been the "correct" API and is unusable here: in English it yields
 * S M T W T F S, three of which are ambiguous.
 *
 * Computed once at module load - the locale cannot change without a reload.
 */
const WEEKDAY_HEADERS: string[] = (() => {
  const MONDAY_UTC = Date.UTC(2024, 0, 1);
  const format = new Intl.DateTimeFormat(undefined, { weekday: 'short', timeZone: 'UTC' });
  return Array.from({ length: 7 }, (_, i) =>
    format.format(new Date(MONDAY_UTC + i * 86_400_000)).slice(0, 2),
  );
})();

/**
 * The month grid. Slots for the visible month are fetched once by the caller
 * and passed down, so nothing here triggers a request.
 *
 * Two things about how availability is drawn, both changed from the version
 * this replaced:
 *
 * **A bookable date is a normal date; an unbookable one is dimmed.** Every
 * available day used to be filled `bg-accent-50`, which turned a normally-open
 * month into a solid block of colour and left the *selected* day with nothing
 * louder to say. Now availability is carried by a small accent dot under the
 * numeral - a second channel besides colour, so it survives greyscale and
 * colour-vision deficiency - and the filled accent circle means one thing only:
 * this is the day you picked.
 *
 * **The past is not reachable.** The month arrows used to walk backwards
 * indefinitely into months that can contain nothing.
 */
export function DateCalendar({
  monthCursor,
  onMonthChange,
  slots,
  loading,
  selectedDateKey,
  onSelectDate,
}: DateCalendarProps) {
  const gridRef = useRef<HTMLDivElement>(null);
  const [focusedKey, setFocusedKey] = useState<string | null>(null);

  const availableDateKeys = useMemo(() => new Set(slots.map((s) => s.localDate)), [slots]);
  const grid = useMemo(() => buildMonthGrid(monthCursor), [monthCursor]);
  // The browser's date, used only to ring today and to stop the month arrows
  // walking backwards. Deliberately NOT part of `isSelectable` - see below.
  const todayKey = toDateKey(new Date());

  /**
   * A day is offered when the backend offered it, and for no other reason.
   *
   * This used to also require `key >= todayKey`, which compared an
   * organizer-local date key against the *visitor's* local date - two different
   * clocks. A guest whose own date runs ahead of the organizer's (Tokyo booking
   * a Los Angeles organizer, at almost any hour) therefore had the earliest
   * genuinely bookable day rendered `disabled`, while it still showed the
   * availability dot that says it is open. Arrow-key navigation skipped it too.
   *
   * The guard could never be right in a way the server was not already:
   * `SlotGenerationService` applies the same-day cutoff, the minimum notice and
   * the booking window against the organizer's own `TimeZoneInfo` before any of
   * this is sent. So availability is exactly what `slots` says it is, and the
   * client does no date arithmetic to second-guess it - which is the timezone
   * contract this codebase already holds everywhere else.
   */
  const isSelectable = (key: string, inMonth: boolean) => inMonth && availableDateKeys.has(key);

  const today = new Date();
  const isAtCurrentMonth =
    monthCursor.getFullYear() === today.getFullYear() && monthCursor.getMonth() === today.getMonth();

  const moveFocus = (fromKey: string, deltaDays: number) => {
    const cells = grid.map((d) => toDateKey(d));
    let index = cells.indexOf(fromKey) + deltaDays;
    while (index >= 0 && index < cells.length) {
      const key = cells[index];
      const inMonth = grid[index].getMonth() === monthCursor.getMonth();
      if (isSelectable(key, inMonth)) {
        setFocusedKey(key);
        gridRef.current?.querySelector<HTMLButtonElement>(`[data-date-key="${key}"]`)?.focus();
        return;
      }
      index += deltaDays > 0 ? 1 : -1;
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent, key: string) => {
    switch (e.key) {
      case 'ArrowRight': e.preventDefault(); moveFocus(key, 1); break;
      case 'ArrowLeft': e.preventDefault(); moveFocus(key, -1); break;
      case 'ArrowDown': e.preventDefault(); moveFocus(key, 7); break;
      case 'ArrowUp': e.preventDefault(); moveFocus(key, -7); break;
    }
  };

  // Where Tab lands: the selected day if there is one, otherwise the first day
  // that can actually be chosen - never a disabled cell.
  const firstSelectableIndex = grid.findIndex((d) => isSelectable(toDateKey(d), d.getMonth() === monthCursor.getMonth()));
  const defaultTabStopKey =
    selectedDateKey && availableDateKeys.has(selectedDateKey)
      ? selectedDateKey
      : firstSelectableIndex >= 0
        ? toDateKey(grid[firstSelectableIndex])
        : null;

  // Six rows of seven. Built here rather than flattened into the grid, because
  // a `role="grid"` whose gridcells are direct children is not a grid - the old
  // markup declared the role and omitted the rows entirely.
  const weeks = Array.from({ length: 6 }, (_, w) => grid.slice(w * 7, w * 7 + 7));

  return (
    <div>
      <div className="mb-3 flex items-center justify-between gap-2">
        <button
          type="button"
          onClick={() => onMonthChange(new Date(monthCursor.getFullYear(), monthCursor.getMonth() - 1, 1))}
          disabled={isAtCurrentMonth}
          className={`rounded-md p-2 text-gray-500 transition-colors hover:bg-gray-100 hover:text-gray-900 disabled:cursor-not-allowed disabled:text-gray-300 disabled:hover:bg-transparent ${FOCUS_RING}`}
          aria-label="Previous month"
        >
          <ChevronLeft className="h-4 w-4" aria-hidden="true" />
        </button>
        <span className="text-[15px] font-semibold text-gray-900" aria-live="polite">
          {monthCursor.toLocaleDateString(undefined, { month: 'long', year: 'numeric' })}
        </span>
        <button
          type="button"
          onClick={() => onMonthChange(new Date(monthCursor.getFullYear(), monthCursor.getMonth() + 1, 1))}
          className={`rounded-md p-2 text-gray-500 transition-colors hover:bg-gray-100 hover:text-gray-900 ${FOCUS_RING}`}
          aria-label="Next month"
        >
          <ChevronRight className="h-4 w-4" aria-hidden="true" />
        </button>
      </div>

      {/* Still `aria-hidden`, deliberately: every date button below already
          carries its full weekday in its accessible name, so announcing a
          two-letter abbreviation as a column header would repeat it in a less
          intelligible form. Keyed by index rather than by label - two locales
          legitimately produce the same two letters for different days. */}
      <div className="grid grid-cols-7 gap-1 pb-1 text-center text-[11px] font-medium tracking-wide text-gray-500 uppercase" aria-hidden="true">
        {WEEKDAY_HEADERS.map((d, i) => (
          <div key={i}>{d}</div>
        ))}
      </div>

      {loading ? (
        <div className="grid grid-cols-7 gap-1" role="status" aria-label="Loading">
          {Array.from({ length: 42 }).map((_, i) => (
            <div key={i} className="h-10 animate-pulse rounded-md bg-gray-100 sm:h-11" />
          ))}
        </div>
      ) : (
        <div ref={gridRef} role="grid" aria-label="Choose a date" className="grid gap-1">
          {weeks.map((week, weekIndex) => (
            <div key={weekIndex} role="row" className="grid grid-cols-7 gap-1">
              {week.map((date) => {
                const key = toDateKey(date);
                const inMonth = date.getMonth() === monthCursor.getMonth();
                const isToday = key === todayKey;
                const selectable = isSelectable(key, inMonth);
                const isSelected = key === selectedDateKey;
                const isTabStop = focusedKey ? key === focusedKey : key === defaultTabStopKey;

                return (
                  <button
                    key={key}
                    type="button"
                    role="gridcell"
                    data-date-key={key}
                    disabled={!selectable}
                    tabIndex={isTabStop ? 0 : -1}
                    onFocus={() => setFocusedKey(key)}
                    onKeyDown={(e) => handleKeyDown(e, key)}
                    onClick={() => onSelectDate(key)}
                    aria-label={date.toLocaleDateString(undefined, {
                      weekday: 'long', month: 'long', day: 'numeric', year: 'numeric',
                    })}
                    aria-selected={selectable ? isSelected : undefined}
                    className={`relative flex h-10 items-center justify-center rounded-md text-sm transition-colors sm:h-11 ${FOCUS_RING} ${
                      isSelected
                        ? 'bg-accent-600 font-semibold text-white'
                        : selectable
                          ? `font-medium text-gray-900 hover:bg-accent-50 ${isToday ? 'ring-1 ring-inset ring-accent-300' : ''}`
                          : `cursor-not-allowed text-gray-300 ${isToday ? 'ring-1 ring-inset ring-gray-200' : ''}`
                    }`}
                  >
                    {date.getDate()}
                    {/* Availability as a mark, not as a fill: it reads at a
                        glance across a whole month without colouring it in,
                        and it is still there in greyscale. */}
                    {selectable && !isSelected && (
                      <span
                        aria-hidden="true"
                        className="absolute bottom-1.5 h-1 w-1 rounded-full bg-accent-500"
                      />
                    )}
                  </button>
                );
              })}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
