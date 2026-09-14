import { CalendarPlus } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { MeetingPanel } from '../components/MeetingPanel';
import { StatusBadge } from '../components/StatusBadge';
import { BookingHeader } from '../components/public/BookingHeader';
import { PublicMessage, PublicShell } from '../components/public/PublicShell';
import { BUTTON_DANGER, BUTTON_SECONDARY, FOCUS_RING, SkeletonLines } from '../components/ui';
import { addMinutesToCalendarTime } from '../lib/calendarDates';
import { downloadIcsFile } from '../lib/ics';
import { useDocumentTitle } from '../lib/pageTitle';
import { formatReminderLabel } from '../lib/reminders';
import { api, errorMessage } from '../lib/api';
import type { PublicBookingDto } from '../lib/types';

/**
 * The guest's own view of their booking - the only screen in this app an
 * organizer's customers reach while signed out, and the one they come back to.
 *
 * It shares `PublicShell` and `BookingHeader` with the wizard on purpose: a
 * guest arriving here from a confirmation email should recognise the page they
 * booked on. It previously had its own `bg-gray-50` backdrop, its own
 * `rounded-2xl` card and its own `bg-gray-900` buttons, none of which the
 * wizard has used since the design system landed - so the journey visibly
 * changed product halfway through.
 */
export function ManageBookingPage() {
  useDocumentTitle('Manage booking');
  const { token = '' } = useParams<{ token: string }>();
  const [booking, setBooking] = useState<PublicBookingDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.bookings
      .getByToken(token)
      .then(setBooking)
      .catch((e) => setError(errorMessage(e, 'This booking could not be loaded.')))
      .finally(() => setLoading(false));
  }, [token]);

  if (loading) {
    return (
      <PublicShell>
        <SkeletonLines lines={6} />
      </PublicShell>
    );
  }

  if (error || !booking) {
    return (
      <PublicMessage title="We couldn't find this booking">
        {error ?? 'The link may be wrong, or the booking may have been removed.'}
      </PublicMessage>
    );
  }

  const canManage = booking.canCancel || booking.canReschedule;

  /**
   * Built from the instants the backend resolved, never from
   * `selectedDate`/`selectedTime` - those are the organizer's wall clock, and
   * parsing them here would read them in the visitor's zone. This is the same
   * rule the wizard's success step follows with `AvailableSlotDto`; the manage
   * page could not follow it until `PublicBookingDto` carried the pair.
   */
  const handleAddToCalendar = () => {
    if (!booking.startUtc || !booking.endUtc) return;
    downloadIcsFile({
      uid: booking.bookingReference,
      title: `${booking.serviceTitle} with ${booking.organizerName}`,
      description: `Booking reference ${booking.bookingReference}`,
      startUtc: booking.startUtc,
      endUtc: booking.endUtc,
    });
  };

  return (
    <PublicShell>
      <BookingHeader
        organizerName={booking.organizerName}
        title={booking.serviceTitle}
        durationMinutes={booking.durationMinutes}
        meetingProvider={booking.meetingProvider}
        timeZoneId={booking.timeZoneId}
        selection={
          booking.selectedDate && booking.selectedTime
            ? {
                date: booking.selectedDate,
                startTime: booking.selectedTime,
                endTime: addMinutesToCalendarTime(booking.selectedTime, booking.durationMinutes),
              }
            : null
        }
        aside={<StatusBadge status={booking.status} />}
        // A cancelled or past booking is not a live one, and the accent panel
        // said otherwise: the same highlighted line for "you are booked" and
        // "this was cancelled".
        tone={canManage ? 'active' : 'inactive'}
      />

      <p className="mt-6 text-[13px] text-gray-500">
        Reference <span className="font-mono font-medium text-gray-900">{booking.bookingReference}</span>
      </p>

      {/* Above the manage actions: joining is what a guest opens this page for
          on the day, while reschedule and cancel are what they open it for
          beforehand. The backend only sends a link while the booking is still
          live, so a cancelled or past booking shows nothing here. */}
      <MeetingPanel
        meetingProvider={booking.meetingProvider}
        meetingUrl={booking.meetingUrl}
        className="mt-6"
      />

      <div className="mt-6 border-t border-gray-200 pt-6">
        {canManage ? (
          <div className="flex flex-wrap items-center gap-2">
            {booking.startUtc && booking.endUtc && (
              <button type="button" onClick={handleAddToCalendar} className={BUTTON_SECONDARY}>
                <CalendarPlus className="h-4 w-4" aria-hidden="true" />
                Add to calendar
              </button>
            )}
            {booking.canReschedule && (
              <Link to={`/manage/${token}/reschedule`} className={BUTTON_SECONDARY}>
                Reschedule
              </Link>
            )}
            {booking.canCancel && (
              <Link to={`/manage/${token}/cancel`} className={BUTTON_DANGER}>
                Cancel booking
              </Link>
            )}
          </div>
        ) : (
          <p className="text-sm text-gray-500">This booking can no longer be changed.</p>
        )}

        {/*
          The actual rule, stated where it is actionable - not a generic "cancel
          any time" policy. `canCancel`/`canReschedule` are computed server-side
          as "still Submitted, and the start is still in the future", so the
          cutoff IS the appointment start and `startUtc` is already on the DTO.
          Nothing here invents a window the backend does not enforce.
        */}
        {canManage && booking.startUtc && (
          <p className="mt-4 text-[13px] text-gray-500">
            You can reschedule or cancel any time before it starts.
          </p>
        )}

        {/*
          Reminders, only when rows for THIS booking actually exist - see
          `reminderLeadMinutes`. One short line, no panel: it is reassurance,
          not an action.
        */}
        {booking.reminderLeadMinutes.length > 0 && (
          <p className="mt-2 text-[13px] text-gray-500">
            We&rsquo;ll email you a reminder{' '}
            {booking.reminderLeadMinutes.map(formatReminderLabel).join(' and ')} before it starts.
          </p>
        )}

        {/*
          The way out, and the reason `organizerEmail` is on this DTO at all.
          The terminal state used to end on "contact the organizer directly" -
          true, and useless, because nothing on the page said how. Shown in both
          states rather than only the terminal one: a guest with a question that
          reschedule and cancel do not answer needs it either way.
        */}
        {booking.organizerEmail && (
          <p className="mt-4 text-[13px] text-gray-500">
            Something else? Email {booking.organizerName} at{' '}
            <a
              href={`mailto:${booking.organizerEmail}`}
              className={`rounded font-medium text-accent-700 underline underline-offset-2 hover:text-accent-900 ${FOCUS_RING}`}
            >
              {booking.organizerEmail}
            </a>
            .
          </p>
        )}
      </div>
    </PublicShell>
  );
}
