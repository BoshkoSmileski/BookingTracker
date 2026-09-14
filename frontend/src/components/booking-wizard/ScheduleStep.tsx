import type { AvailableSlotDto, BookingInstructionDto } from '../../lib/types';
import { BookingInstructions } from './BookingInstructions';
import { SlotPicker } from './SlotPicker';

interface ScheduleStepProps {
  instructions: BookingInstructionDto[];
  monthCursor: Date;
  onMonthChange: (next: Date) => void;
  slots: AvailableSlotDto[];
  loading: boolean;
  error?: string | null;
  onRetry?: () => void;
  selectedDateKey: string | null;
  onSelectDate: (dateKey: string) => void;
  selectedSlot: AvailableSlotDto | null;
  onSelectSlot: (slot: AvailableSlotDto) => void;
  timeZoneId: string;
}

/**
 * Step one: read anything the organizer wants read, then choose when.
 *
 * The instructions sit **above** the picker rather than below it, which is the
 * same position they held on the step this replaced and for the same reason -
 * "please have your account number ready" is useless news to someone who has
 * already spent two minutes choosing a slot. They render nothing at all for a
 * page that sets none, so most pages open straight onto the calendar.
 */
export function ScheduleStep({ instructions, ...pickerProps }: ScheduleStepProps) {
  return (
    <div>
      <BookingInstructions instructions={instructions} heading="Before you book" className="mb-8" />
      <SlotPicker {...pickerProps} />
    </div>
  );
}
