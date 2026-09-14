import { CalendarPlus, Check } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { addMinutesToCalendarTime, formatCalendarTime } from '../../lib/calendarDates';
import { downloadIcsFile } from '../../lib/ics';
import type { AvailableSlotDto, BookingConfirmationDto, BookingPageDto } from '../../lib/types';
import { MeetingPanel } from '../MeetingPanel';
import { BUTTON_SECONDARY, FOCUS_RING } from '../ui';

interface SuccessStepProps {
  page: BookingPageDto;
  confirmation: BookingConfirmationDto;
  /**
   * The slot that was booked, as a real UTC instant.
   *
   * Passed down rather than reconstructed, because reconstructing it is the bug
   * this fixes: the calendar file used to be built by reading the organizer's
   * wall clock as if it were the *visitor's* local time, so a guest in another
   * zone downloaded an appointment at the wrong hour. `AvailableSlotDto` already
   * carries the instant the backend computed with the organizer's own
   * `TimeZoneInfo`; using it means no timezone arithmetic happens here at all.
   */
  slot?: AvailableSlotDto | null;
}

/**
 * Done.
 *
 * The persistent booking header directly above this already states what was
 * booked, for when and where, so this screen deliberately does **not** repeat
 * it in a summary list of its own - it says the booking succeeded, gives the
 * reference, and puts every next action in one place.
 *
 * Those actions are ordered by when a guest needs them: join (on the day), add
 * to their calendar (now), then manage/reschedule/cancel (in between). The
 * manage link is offered explicitly rather than left to the email, so nothing
 * here implies the confirmation email is the only way back to the booking.
 */
export function SuccessStep({ page, confirmation, slot = null }: SuccessStepProps) {
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    const id = requestAnimationFrame(() => setVisible(true));
    return () => cancelAnimationFrame(id);
  }, []);

  const token = confirmation.publicToken;
  const time = confirmation.selectedTime;

  const handleAddToCalendar = () => {
    if (!slot) return;
    downloadIcsFile({
      uid: confirmation.id,
      title: `${page.title} with ${page.organizerName}`,
      description: `Booking reference ${confirmation.bookingReference}`,
      startUtc: slot.startUtc,
      endUtc: slot.endUtc,
    });
  };

  return (
    <div>
      <div className="flex items-start gap-4">
        {/* The accent, not a green disc. The design system's `success` tone is
            the accent (see NOTICE_TONE in ui.tsx) and a second colour arriving
            on the last screen of the flow is exactly the "one accent" rule
            being broken where it is most visible. */}
        <span
          className={`flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-accent-600 text-white transition-all duration-300 ${
            visible ? 'scale-100 opacity-100' : 'scale-75 opacity-0'
          }`}
        >
          <Check className="h-6 w-6" aria-hidden="true" strokeWidth={2.5} />
        </span>
        <div className="min-w-0">
          <h2 className="text-xl font-semibold tracking-tight text-gray-900">Booking confirmed</h2>
          <p className="mt-1 text-[15px] leading-relaxed text-gray-500">
            {confirmation.guestConfirmationQueued && confirmation.email ? (
              // "We've emailed the details" was not true yet: submitting queues
              // the confirmation and EmailQueueProcessor sends it seconds later,
              // out of this request. A guest has no use for the word "queued",
              // so this says the same thing in the tense the app can actually
              // stand behind - the wording the reschedule screen already used.
              //
              // Gated on what the server reports rather than on an address
              // being present: an organizer with guest notifications switched
              // off produces no confirmation at all, and this line was
              // promising one regardless.
              <>
                A confirmation email is on its way to{' '}
                <span className="font-medium text-gray-700">{confirmation.email}</span>.
              </>
            ) : (
              <>Your appointment is booked.</>
            )}{' '}
            {token && 'You can change or cancel it any time from the link below.'}
          </p>
          {confirmation.bookingReference && (
            <p className="mt-3 text-[13px] text-gray-500">
              Reference{' '}
              <span className="font-mono font-medium text-gray-900">{confirmation.bookingReference}</span>
            </p>
          )}
        </div>
      </div>

      {/* The last step of the lifecycle: the guest can join - and bookmark -
          the meeting they just booked without going to find the confirmation
          email. Present because calendar sync now runs before the submit
          response is built, so the link is already on the DTO. Renders nothing
          when the booking has no meeting. */}
      <MeetingPanel
        meetingProvider={confirmation.meetingProvider}
        meetingUrl={confirmation.meetingUrl}
        className="mt-6"
      />

      <div className="mt-6 flex flex-wrap gap-2 border-t border-gray-200 pt-6">
        {slot && time && (
          <button
            type="button"
            onClick={handleAddToCalendar}
            aria-label={`Add to calendar — downloads an appointment file for ${formatCalendarTime(time)}–${addMinutesToCalendarTime(time, page.durationMinutes)}`}
            className={BUTTON_SECONDARY}
          >
            <CalendarPlus className="h-4 w-4" aria-hidden="true" />
            Add to calendar
          </button>
        )}
        {token && (
          <Link to={`/manage/${token}`} className={BUTTON_SECONDARY}>
            Manage booking
          </Link>
        )}
      </div>

      {token && (
        <p className="mt-4 text-[13px] text-gray-500">
          Need to change something?{' '}
          <Link
            to={`/manage/${token}/reschedule`}
            className={`rounded font-medium text-accent-700 underline underline-offset-2 hover:text-accent-900 ${FOCUS_RING}`}
          >
            Reschedule
          </Link>{' '}
          or{' '}
          <Link
            to={`/manage/${token}/cancel`}
            className={`rounded font-medium text-accent-700 underline underline-offset-2 hover:text-accent-900 ${FOCUS_RING}`}
          >
            cancel
          </Link>
          .
        </p>
      )}
    </div>
  );
}
