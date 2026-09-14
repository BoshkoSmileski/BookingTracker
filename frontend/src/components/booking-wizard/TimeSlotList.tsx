import { formatCalendarTime } from '../../lib/calendarDates';
import { formatUtcOnViewerClock, isViewerTimeZone } from '../../lib/timezone';
import type { AvailableSlotDto } from '../../lib/types';
import { FOCUS_RING } from '../ui';

interface TimeSlotListProps {
  slots: AvailableSlotDto[];
  selectedSlot: AvailableSlotDto | null;
  onSelectSlot: (slot: AvailableSlotDto) => void;
  /**
   * The organizer's zone, used only to decide whether a second reading is worth
   * showing. Optional because the organizer's own dashboard reuses this picker
   * to reschedule, and there the viewer *is* the organizer - a "your time"
   * reading of their own clock is noise, and no screen there loads a schedule
   * just to be told so.
   */
  timeZoneId?: string | null;
}

/**
 * The times available on the chosen day.
 *
 * Two corrections from the version this replaced, and the first is not
 * cosmetic:
 *
 * **The label is the organizer's wall clock.** It used to be
 * `new Date(slot.startUtc).toLocaleTimeString()` - the *visitor's* zone - while
 * `BookingPage` recorded `slot.localStartTime`, the organizer's. A guest in New
 * York booking a Skopje organizer therefore clicked a button reading 04:00 and
 * was told on the very next screen they had booked 10:00. Both numbers were
 * right; the screen never said whose clock either was on. Everything this
 * system stores, emails and writes into the `.ics` is organizer-local, so that
 * is what the button says - and the visitor's own reading appears underneath
 * it, labelled, only when the two zones actually differ.
 *
 * **They are toggle buttons, not a listbox.** The old markup carried
 * `role="listbox"`/`role="option"` with `aria-selected={false}` hardcoded on
 * every option, promising a widget with arrow-key navigation and a selected
 * option that did not exist - the same fake-ARIA problem seen one screen over. A group of buttons carrying `aria-pressed`
 * is what these actually are.
 */
export function TimeSlotList({ slots, selectedSlot, onSelectSlot, timeZoneId }: TimeSlotListProps) {
  const showViewerClock = Boolean(timeZoneId) && !isViewerTimeZone(timeZoneId!);

  return (
    // Three across once the two panels are side by side, so a full working day
    // of 15-minute slots is nine rows beside a six-row calendar rather than
    // thirteen - two columns left the calendar column empty for 400px.
    <div role="group" aria-label="Available times" className="grid grid-cols-3 gap-2 sm:grid-cols-4 md:grid-cols-3">
      {slots.map((slot) => {
        const label = formatCalendarTime(slot.localStartTime);
        const isSelected = selectedSlot?.startUtc === slot.startUtc;

        return (
          <button
            key={slot.startUtc}
            type="button"
            aria-pressed={isSelected}
            onClick={() => onSelectSlot(slot)}
            className={`flex min-h-11 flex-col items-center justify-center rounded-lg border px-2 py-2 text-sm font-medium transition-colors ${FOCUS_RING} ${
              isSelected
                ? 'border-accent-600 bg-accent-600 text-white'
                : 'border-gray-300 bg-white text-gray-900 hover:border-accent-500 hover:bg-accent-50 hover:text-accent-800'
            }`}
          >
            {label}
            {showViewerClock && (
              <span className={`text-[11px] font-normal ${isSelected ? 'text-accent-100' : 'text-gray-500'}`}>
                <span className="sr-only">, </span>
                {formatUtcOnViewerClock(slot.startUtc)} your time
              </span>
            )}
          </button>
        );
      })}
    </div>
  );
}
