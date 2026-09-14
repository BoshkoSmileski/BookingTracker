import { Check } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { RescheduleFlow } from '../components/RescheduleFlow';
import { BookingHeader } from '../components/public/BookingHeader';
import { PublicMessage, PublicShell } from '../components/public/PublicShell';
import { BUTTON_SECONDARY, InlineNotice, SECTION_HEADING, SkeletonLines } from '../components/ui';
import {
  addMinutesToCalendarTime,
  formatCalendarDateLong,
  formatCalendarTimeRange,
} from '../lib/calendarDates';
import { api, errorMessage } from '../lib/api';
import type { AvailableSlotDto, BookingConfirmationDto, PublicBookingDto } from '../lib/types';
import { useDocumentTitle } from '../lib/pageTitle';

/**
 * Moving a booking to a new time.
 *
 * The header keeps the **current** booking on screen while a new slot is
 * chosen, which is the one piece of context this screen cannot do without -
 * "reschedule" is a comparison, and the old time used to be a grey sentence of
 * raw ISO values that disappeared as soon as the picker rendered.
 */
export function RescheduleBookingPage() {
  useDocumentTitle('Reschedule booking');
  const { token = '' } = useParams<{ token: string }>();
  const navigate = useNavigate();
  const [booking, setBooking] = useState<PublicBookingDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [rescheduled, setRescheduled] = useState<AvailableSlotDto | null>(null);
  /** The move's response, for the one thing this screen needs from it: whether a guest confirmation was actually queued. */
  const [moveResult, setMoveResult] = useState<BookingConfirmationDto | null>(null);

  useEffect(() => {
    api.bookings
      .getByToken(token)
      .then(setBooking)
      .catch((e) => setLoadError(errorMessage(e, 'This booking could not be loaded.')))
      .finally(() => setLoading(false));
  }, [token]);

  const handleConfirm = async (slot: AvailableSlotDto) => {
    setSubmitting(true);
    setError(null);
    try {
      setMoveResult(await api.bookings.reschedule(token, slot.localDate, slot.localStartTime));
      setRescheduled(slot);
    } catch (e) {
      setError(errorMessage(e, 'We could not move this booking. Please try again.'));
    } finally {
      setSubmitting(false);
    }
  };

  if (loading) {
    return (
      <PublicShell>
        <SkeletonLines lines={6} />
      </PublicShell>
    );
  }

  if (loadError || !booking) {
    return (
      <PublicMessage title="We couldn't find this booking">
        {loadError ?? 'The link may be wrong, or the booking may have been removed.'}
      </PublicMessage>
    );
  }

  // Once moved, the header shows the NEW slot - it is the booking now, and
  // leaving the old time up would be the one screen in the flow still
  // describing something that is no longer true.
  const selection = rescheduled
    ? {
        date: rescheduled.localDate,
        startTime: rescheduled.localStartTime,
        endTime: rescheduled.localEndTime,
        startUtc: rescheduled.startUtc,
        endUtc: rescheduled.endUtc,
      }
    : booking.selectedDate && booking.selectedTime
      ? {
          date: booking.selectedDate,
          startTime: booking.selectedTime,
          endTime: addMinutesToCalendarTime(booking.selectedTime, booking.durationMinutes),
        }
      : null;

  return (
    <PublicShell>
      <BookingHeader
        organizerName={booking.organizerName}
        title={booking.serviceTitle}
        durationMinutes={booking.durationMinutes}
        meetingProvider={booking.meetingProvider}
        timeZoneId={booking.timeZoneId}
        selection={selection}
      />

      <div className="mt-8 border-t border-gray-200 pt-6">
        {rescheduled ? (
          <div>
            <div className="flex items-start gap-4">
              <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-accent-600 text-white">
                <Check className="h-6 w-6" aria-hidden="true" strokeWidth={2.5} />
              </span>
              <div>
                <h2 className="text-xl font-semibold tracking-tight text-gray-900">Booking moved</h2>
                <p className="mt-1 text-[15px] leading-relaxed text-gray-500">
                  You're now booked for {formatCalendarDateLong(rescheduled.localDate)},{' '}
                  {formatCalendarTimeRange(rescheduled.localStartTime, rescheduled.localEndTime)}.
                  {/* Only claimed when the server says one was queued - see CancelBookingPage. */}
                  {moveResult?.guestConfirmationQueued
                    ? ` A confirmation is on its way${booking.email ? ` to ${booking.email}` : ''}.`
                    : ''}
                </p>
              </div>
            </div>
            <Link to={`/manage/${token}`} className={`${BUTTON_SECONDARY} mt-6`}>
              View booking
            </Link>
          </div>
        ) : !booking.canReschedule ? (
          <>
            <InlineNotice tone="warning">This booking can no longer be rescheduled.</InlineNotice>
            <Link to={`/manage/${token}`} className={`${BUTTON_SECONDARY} mt-4`}>
              Back to booking
            </Link>
          </>
        ) : (
          <>
            <h2 className={`${SECTION_HEADING} mb-1`}>Pick a new time</h2>
            <p className="mb-6 text-sm text-gray-500">
              Your current time is held until you confirm the move.
            </p>

            {error && (
              <InlineNotice tone="error" className="mb-6">
                {error}
              </InlineNotice>
            )}

            <RescheduleFlow
              slug={booking.bookingPageSlug}
              timeZoneId={booking.timeZoneId}
              submitting={submitting}
              onConfirm={(slot) => void handleConfirm(slot)}
              onCancel={() => navigate(`/manage/${token}`)}
            />
          </>
        )}
      </div>
    </PublicShell>
  );
}
