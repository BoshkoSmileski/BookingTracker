import { useId, useMemo, useRef } from 'react';
import { formatCalendarDateLong } from '../../lib/calendarDates';
import type { AvailableSlotDto } from '../../lib/types';
import { BUTTON_SECONDARY, LoadError, SECTION_HEADING } from '../ui';
import { DateCalendar } from './DateCalendar';
import { TimeSlotList } from './TimeSlotList';

interface SlotPickerProps {
  monthCursor: Date;
  onMonthChange: (next: Date) => void;
  slots: AvailableSlotDto[];
  loading: boolean;
  /**
   * The times could not be fetched. Distinct from an empty `slots`, which means
   * the organizer genuinely has nothing free - see `useAvailableSlots`.
   */
  error?: string | null;
  onRetry?: () => void;
  selectedDateKey: string | null;
  onSelectDate: (dateKey: string) => void;
  selectedSlot: AvailableSlotDto | null;
  onSelectSlot: (slot: AvailableSlotDto) => void;
  /** The organizer's zone. Optional - see the note on `TimeSlotList`. */
  timeZoneId?: string | null;
}

/** Below this the two panels stack, so the times need scrolling to. Mirrors Tailwind's `md`. */
const STACKED_BREAKPOINT_PX = 768;

/**
 * Choosing when - the central interaction of the whole product, and now one
 * screen rather than two.
 *
 * Date and time used to be separate wizard steps: picking a day **replaced**
 * the calendar with a list of times, so comparing Tuesday's times against
 * Wednesday's meant going Back, re-picking, and losing your place. Side by side
 * they are what they actually are - one question with two parts - and the
 * calendar stays on screen as the thing you change your mind with.
 *
 * Built once and used by both the booking wizard and the public reschedule
 * flow, the same way the two steps it replaced were.
 *
 * On a phone there is no room for two columns, so they stack and the time list
 * is scrolled to when a date is chosen: the answer to "what do I do next?"
 * should not be below the fold.
 */
export function SlotPicker({
  monthCursor,
  onMonthChange,
  slots,
  loading,
  error,
  onRetry,
  selectedDateKey,
  onSelectDate,
  selectedSlot,
  onSelectSlot,
  timeZoneId,
}: SlotPickerProps) {
  const dateHeadingId = useId();
  const timeHeadingId = useId();
  const timePanelRef = useRef<HTMLDivElement>(null);

  const daySlots = useMemo(
    () => (selectedDateKey ? slots.filter((s) => s.localDate === selectedDateKey) : []),
    [slots, selectedDateKey],
  );
  const hasAnyAvailability = slots.length > 0;

  const handleSelectDate = (dateKey: string) => {
    onSelectDate(dateKey);
    if (window.innerWidth < STACKED_BREAKPOINT_PX) {
      timePanelRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  };

  const goToNextMonth = () =>
    onMonthChange(new Date(monthCursor.getFullYear(), monthCursor.getMonth() + 1, 1));

  // Above both panels rather than inside the times column: the calendar is
  // equally wrong when this happens (a month with no dots reads as a month with
  // no availability), and neither panel is the place to explain it.
  if (error) {
    return (
      <LoadError
        message={error}
        onRetry={onRetry}
      />
    );
  }

  return (
    <div className="grid gap-8 md:grid-cols-2 md:gap-8">
      <section aria-labelledby={dateHeadingId}>
        <h2 id={dateHeadingId} className={`mb-4 ${SECTION_HEADING}`}>
          Choose a date
        </h2>
        <DateCalendar
          monthCursor={monthCursor}
          onMonthChange={onMonthChange}
          slots={slots}
          loading={loading}
          selectedDateKey={selectedDateKey}
          onSelectDate={handleSelectDate}
        />
      </section>

      <section aria-labelledby={timeHeadingId} ref={timePanelRef} className="scroll-mt-4">
        {/* The heading carries the chosen day rather than repeating it in a
            line underneath, and is announced when it changes - on a phone the
            calendar has just scrolled out of view, so the heading is the only
            thing left saying which day these times belong to. */}
        <h2 id={timeHeadingId} aria-live="polite" className={`mb-4 ${SECTION_HEADING}`}>
          {selectedDateKey ? `Times on ${formatCalendarDateLong(selectedDateKey)}` : 'Available times'}
        </h2>

        {loading ? (
          <div className="grid grid-cols-3 gap-2 sm:grid-cols-4 md:grid-cols-3" role="status" aria-label="Loading">
            {Array.from({ length: 8 }).map((_, i) => (
              <div key={i} className="h-11 animate-pulse rounded-lg bg-gray-100" />
            ))}
          </div>
        ) : !hasAnyAvailability ? (
          <SlotNotice
            title={`Nothing available in ${monthCursor.toLocaleDateString(undefined, { month: 'long' })}`}
            description="There are no open times this month."
            action={
              <button type="button" onClick={goToNextMonth} className={BUTTON_SECONDARY}>
                Try the next month
              </button>
            }
          />
        ) : !selectedDateKey ? (
          <SlotNotice
            title="Pick a date"
            description="Days with times available are marked with a dot."
          />
        ) : daySlots.length === 0 ? (
          <SlotNotice
            title="No times on this date"
            description="Choose another day — the ones with a dot still have room."
          />
        ) : (
          <TimeSlotList
            slots={daySlots}
            selectedSlot={selectedSlot}
            onSelectSlot={onSelectSlot}
            timeZoneId={timeZoneId}
          />
        )}
      </section>
    </div>
  );
}

/**
 * The times panel with nothing in it - which is a normal state, not a failure,
 * so it says what to do rather than what went wrong.
 *
 * Deliberately lighter than the shared `EmptyState`: that reserves ~170px of
 * dashed box even when compact, which beside a calendar of the same height
 * reads as a second panel competing with it rather than as an absence.
 */
function SlotNotice({
  title,
  description,
  action,
}: {
  title: string;
  description: string;
  action?: React.ReactNode;
}) {
  return (
    <div className="rounded-lg border border-dashed border-gray-300 px-5 py-8 text-center">
      <p className="text-sm font-medium text-gray-900">{title}</p>
      <p className="mx-auto mt-1 max-w-xs text-sm leading-relaxed text-gray-500">{description}</p>
      {action && <div className="mt-4 flex justify-center">{action}</div>}
    </div>
  );
}
