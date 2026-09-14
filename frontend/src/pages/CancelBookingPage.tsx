import { Check } from 'lucide-react';
import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { BookingHeader } from '../components/public/BookingHeader';
import { PublicMessage, PublicShell } from '../components/public/PublicShell';
import {
  BUTTON_DANGER_SOLID, BUTTON_SECONDARY, Field, FOCUS_RING, InlineNotice, INPUT,
  SECTION_HEADING, SkeletonLines
} from '../components/ui';
import { addMinutesToCalendarTime } from '../lib/calendarDates';
import { api, errorMessage } from '../lib/api';
import type { BookingConfirmationDto, PublicBookingDto } from '../lib/types';
import { useDocumentTitle } from '../lib/pageTitle';

/**
 * Cancelling, with the booking being cancelled visible the whole time.
 *
 * The screen used to reduce it to one grey sentence of raw ISO values -
 * "30 Minute Meeting with Demo Organizer on 2026-08-20 at 09:00" - directly
 * above a red button. Confirming a destructive action against a date you have
 * to parse is the wrong way round; the header states it in full, and the
 * decision sits underneath.
 */
export function CancelBookingPage() {
  useDocumentTitle('Cancel booking');
  const { token = '' } = useParams<{ token: string }>();
  const navigate = useNavigate();
  const [booking, setBooking] = useState<PublicBookingDto | null>(null);
  const [reason, setReason] = useState('');
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  /**
   * The cancel response, kept rather than reduced to a boolean, because what
   * this screen says next depends on `guestConfirmationQueued` - whether a
   * confirmation email was actually written to the queue. It is `null` until
   * the cancellation succeeds, so it doubles as the "cancelled" flag.
   */
  const [cancelled, setCancelled] = useState<BookingConfirmationDto | null>(null);

  useEffect(() => {
    api.bookings
      .getByToken(token)
      .then(setBooking)
      .catch((e) => setLoadError(errorMessage(e, 'This booking could not be loaded.')))
      .finally(() => setLoading(false));
  }, [token]);

  const handleCancel = async () => {
    setSubmitting(true);
    setError(null);
    try {
      setCancelled(await api.bookings.cancel(token, reason || undefined));
    } catch (e) {
      setError(errorMessage(e, 'We could not cancel this booking. Please try again.'));
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

  const selection =
    booking.selectedDate && booking.selectedTime
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
        // Neutral the moment the booking stops being live - either because it
        // has just been cancelled, or because it never could be.
        tone={cancelled || !booking.canCancel ? 'inactive' : 'active'}
      />

      <div className="mt-8 border-t border-gray-200 pt-6">
        {cancelled ? (
          <div className="flex items-start gap-4">
            <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-accent-600 text-white">
              <Check className="h-6 w-6" aria-hidden="true" strokeWidth={2.5} />
            </span>
            <div>
              <h2 className="text-xl font-semibold tracking-tight text-gray-900">Booking cancelled</h2>
              <p className="mt-1 text-[15px] leading-relaxed text-gray-500">
                {/*
                  "on its way", never "sent" - see SuccessStep for the tense.
                  And only when the server says a guest confirmation was
                  actually queued: an organizer with guest notifications
                  switched off produces no email at all, and this used to
                  promise one anyway on the strength of an address being on
                  file.
                */}
                {cancelled.guestConfirmationQueued && booking.email
                  ? `We've let ${booking.organizerName} know, and a confirmation email is on its way to ${booking.email}.`
                  : `We've let ${booking.organizerName} know.`}
              </p>
              {booking.organizerEmail && (
                <p className="mt-3 text-[13px] text-gray-500">
                  Need to talk to them? Email{' '}
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
          </div>
        ) : !booking.canCancel ? (
          <>
            <InlineNotice tone="warning">This booking can no longer be cancelled.</InlineNotice>
            <Link to={`/manage/${token}`} className={`${BUTTON_SECONDARY} mt-4`}>
              Back to booking
            </Link>
          </>
        ) : (
          <>
            <h2 className={`${SECTION_HEADING} mb-1`}>Cancel this booking?</h2>
            <p className="mb-6 text-sm text-gray-500">
              {booking.organizerName} will be notified, and the time will be offered to someone else.
            </p>

            {/* Spelled `META` out by hand and wired its own `aria-describedby`. */}
            <Field label="Reason" optional hint={`Shared with ${booking.organizerName} only.`}>
              {(control) => (
                <textarea
                  {...control}
                  rows={3}
                  className={`${INPUT} w-full`}
                  value={reason}
                  onChange={(e) => setReason(e.target.value)}
                  placeholder="Anything you'd like the organizer to know"
                />
              )}
            </Field>

            {error && (
              <InlineNotice tone="error" className="mt-5">
                {error}
              </InlineNotice>
            )}

            {/* Keeping the booking first in the DOM and visually last on wide
                screens: the safe option should be the easy one to reach, and
                the destructive one should never be what a stray Enter hits. */}
            <div className="mt-6 flex flex-col-reverse gap-3 sm:flex-row-reverse sm:justify-end">
              <button
                type="button"
                onClick={() => void handleCancel()}
                disabled={submitting}
                className={BUTTON_DANGER_SOLID}
              >
                {submitting ? 'Cancelling…' : 'Cancel booking'}
              </button>
              <button
                type="button"
                onClick={() => navigate(`/manage/${token}`)}
                disabled={submitting}
                className={BUTTON_SECONDARY}
              >
                Keep booking
              </button>
            </div>
          </>
        )}
      </div>
    </PublicShell>
  );
}
