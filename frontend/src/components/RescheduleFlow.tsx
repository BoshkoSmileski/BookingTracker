import { useState } from 'react';
import { SlotPicker } from './booking-wizard/SlotPicker';
import { useAvailableSlots } from '../hooks/useAvailableSlots';
import type { AvailableSlotDto } from '../lib/types';
import { BUTTON_PRIMARY, BUTTON_SECONDARY } from './ui';
import { formatCalendarDateLong, formatCalendarTimeRange } from '../lib/calendarDates';

interface RescheduleFlowProps {
  slug: string;
  /** The organizer's zone - the clock the offered times are on. Optional; see `TimeSlotList`. */
  timeZoneId?: string | null;
  onConfirm: (slot: AvailableSlotDto) => void;
  onCancel: () => void;
  submitting?: boolean;
}

/**
 * Moving an existing booking to a new slot.
 *
 * Reuses `SlotPicker` - the same calendar-and-times pair the booking wizard
 * uses - so the central interaction exists once and a guest rescheduling sees
 * exactly the screen they booked on. It previously drove `DateStep` and
 * `TimeStep` through a two-phase state machine of its own, which meant it also
 * inherited their "picking a date replaces the calendar" problem.
 *
 * Unlike the wizard, choosing a time here does **not** commit: a reschedule
 * moves something that already exists, so the new slot is selected and then
 * confirmed explicitly. The wizard can afford to advance on click because it
 * has two more steps in which to change your mind; this has none.
 */
export function RescheduleFlow({ slug, timeZoneId, onConfirm, onCancel, submitting }: RescheduleFlowProps) {
  const [monthCursor, setMonthCursor] = useState(() => {
    const d = new Date();
    d.setDate(1);
    return d;
  });
  const [selectedDateKey, setSelectedDateKey] = useState<string | null>(null);
  const [selectedSlot, setSelectedSlot] = useState<AvailableSlotDto | null>(null);

  const { slots, loading, error, reload } = useAvailableSlots(slug, monthCursor);

  return (
    <div>
      <SlotPicker
        monthCursor={monthCursor}
        onMonthChange={setMonthCursor}
        slots={slots}
        loading={loading}
        error={error}
        onRetry={reload}
        selectedDateKey={selectedDateKey}
        onSelectDate={(dateKey) => {
          setSelectedDateKey(dateKey);
          setSelectedSlot(null);
        }}
        selectedSlot={selectedSlot}
        onSelectSlot={setSelectedSlot}
        timeZoneId={timeZoneId}
      />

      <div className="mt-8 flex flex-col-reverse gap-3 border-t border-gray-200 pt-6 sm:flex-row sm:items-center sm:justify-between">
        <button type="button" onClick={onCancel} disabled={submitting} className={BUTTON_SECONDARY}>
          Keep current time
        </button>
        <div className="sm:text-right">
          <button
            type="button"
            onClick={() => selectedSlot && onConfirm(selectedSlot)}
            disabled={!selectedSlot || submitting}
            className={`${BUTTON_PRIMARY} w-full sm:w-auto`}
          >
            {submitting ? 'Moving booking…' : 'Move booking'}
          </button>
          <p className="mt-2 text-[13px] text-gray-500">
            {selectedSlot
              ? `Moves to ${formatCalendarDateLong(selectedSlot.localDate)}, ${formatCalendarTimeRange(selectedSlot.localStartTime, selectedSlot.localEndTime)}.`
              : 'Pick a new date and time to continue.'}
          </p>
        </div>
      </div>
    </div>
  );
}
