import { useCallback, useMemo, useState } from 'react';
import { useParams } from 'react-router-dom';
import { BookingHeader, type BookingSelection } from '../components/public/BookingHeader';
import { PublicMessage, PublicShell } from '../components/public/PublicShell';
import { DetailsStep } from '../components/booking-wizard/DetailsStep';
import { ReviewStep } from '../components/booking-wizard/ReviewStep';
import { ScheduleStep } from '../components/booking-wizard/ScheduleStep';
import { StepProgress, type WizardStep } from '../components/booking-wizard/StepProgress';
import { SuccessStep } from '../components/booking-wizard/SuccessStep';
import { useAvailableSlots } from '../hooks/useAvailableSlots';
import { useBookingSessionTracker } from '../hooks/useBookingSessionTracker';
import { addMinutesToCalendarTime } from '../lib/calendarDates';
import type { AvailableSlotDto } from '../lib/types';
import { useDocumentTitle } from '../lib/pageTitle';

/**
 * The public booking flow: Time -> Details -> Confirm, then the confirmation.
 *
 * **Three steps, not six.** It used to open on a "select a service" step for a
 * page offering exactly one service, then split choosing a day and choosing a
 * time across two screens that replaced one another. Service is now simply
 * stated in the header (it was never a choice), and date and time are one
 * screen, because they are one decision - see `SlotPicker`.
 *
 * **The header is the summary.** `BookingHeader` sits above every step and
 * absorbs the chosen slot as soon as there is one, so "what am I booking?" is
 * answered continuously rather than only on a review screen, and "Change" is
 * reachable from anywhere in the flow.
 *
 * Every interaction still flows through `useBookingSessionTracker` exactly as
 * before - a slot pick fires DateSelected/TimeSelected the moment it happens,
 * every keystroke is its own FieldChanged - so the event log, the rebuild
 * endpoint and the analytics funnel are untouched by any of this.
 */
export function BookingPage() {
  const { slug = '' } = useParams<{ slug: string }>();
  const tracker = useBookingSessionTracker(slug);

  const [step, setStep] = useState<WizardStep>('time');
  const [monthCursor, setMonthCursor] = useState(() => {
    const d = new Date();
    d.setDate(1);
    return d;
  });
  const [selectedDateKey, setSelectedDateKey] = useState<string | null>(null);
  // Kept as the whole DTO, not just the wall-clock strings: `startUtc`/`endUtc`
  // are what let the header show a visitor in another zone their own reading,
  // and what make the downloadable calendar file land on the right instant.
  const [selectedSlot, setSelectedSlot] = useState<AvailableSlotDto | null>(null);

  // Fetched once per (slug, month) and shared by the calendar and the time
  // list, which now sit side by side - so nothing refetches when a date changes.
  const { slots, loading: slotsLoading, error: slotsError, reload: reloadSlots } =
    useAvailableSlots(slug, monthCursor);

  const handleSelectSlot = useCallback(
    (slot: AvailableSlotDto) => {
      setSelectedSlot(slot);
      tracker.selectSlot(slot.localDate, slot.localStartTime);
      setStep('details');
    },
    [tracker],
  );

  /** Back to the calendar with the slot released - what "Change" and a booking conflict both need. */
  const handleChangeTime = useCallback(() => {
    setSelectedSlot(null);
    setStep('time');
  }, []);

  const handleConfirm = useCallback(async () => {
    const success = await tracker.submit();
    if (success) setStep('success');
  }, [tracker]);

  const { page, formState } = tracker;

  // Named after what is being booked and with whom, which is what makes a
  // pinned tab or a bookmark useful to a guest. `null` until the page loads:
  // the tab reads "BookingTracker" for that moment rather than flashing a
  // placeholder or a slug.
  useDocumentTitle(page ? `${page.title} — ${page.organizerName}` : null);

  // The selection the header shows. Built from the picked slot while one is
  // held, and otherwise from the session itself - which is what survives a
  // reload mid-booking, since the tracker restores selectedDate/selectedTime
  // from the server but has no slot DTO to restore.
  const selection: BookingSelection | null = useMemo(() => {
    if (selectedSlot) {
      return {
        date: selectedSlot.localDate,
        startTime: selectedSlot.localStartTime,
        endTime: selectedSlot.localEndTime,
        startUtc: selectedSlot.startUtc,
        endUtc: selectedSlot.endUtc,
      };
    }
    if (!page || !formState.selectedDate || !formState.selectedTime) return null;
    return {
      date: formState.selectedDate,
      startTime: formState.selectedTime,
      endTime: addMinutesToCalendarTime(formState.selectedTime, page.durationMinutes),
    };
  }, [selectedSlot, page, formState.selectedDate, formState.selectedTime]);

  if (tracker.loading) return <BookingPageSkeleton />;

  if (tracker.error || !page || !tracker.session) {
    return (
      <PublicMessage title="This booking page isn't available">
        {/* A 404 here carries the API's own "BookingPage with key 'x' was not
            found" - a developer's sentence, naming an internal type, shown to
            somebody who followed a link a friend sent them. Every other
            failure is worth passing through verbatim, because it is either the
            server explaining itself usefully or the connectivity wording. */}
        {tracker.errorIsNotFound || !tracker.error
          ? 'The link may be wrong, or the organizer may have turned this page off.'
          : tracker.error}
      </PublicMessage>
    );
  }

  return (
    <PublicShell>
      <BookingHeader
        organizerName={page.organizerName}
        title={page.title}
        description={page.description}
        durationMinutes={page.durationMinutes}
        meetingProvider={page.meetingProvider}
        timeZoneId={page.timeZoneId}
        selection={selection}
        // Not on the confirmation: the slot is booked by then, and changing it
        // is a reschedule, which the success step offers as its own action.
        onChangeSelection={step === 'success' ? undefined : handleChangeTime}
      />

      {step !== 'success' && <StepProgress currentStep={step} onGoToStep={setStep} />}

      <div key={step} className="mt-8 animate-[fadeIn_200ms_ease-out]">
        {step === 'time' && (
          <ScheduleStep
            instructions={page.instructions}
            monthCursor={monthCursor}
            onMonthChange={setMonthCursor}
            slots={slots}
            loading={slotsLoading}
            error={slotsError}
            onRetry={reloadSlots}
            selectedDateKey={selectedDateKey}
            onSelectDate={setSelectedDateKey}
            selectedSlot={selectedSlot}
            onSelectSlot={handleSelectSlot}
            timeZoneId={page.timeZoneId}
          />
        )}

        {step === 'details' && (
          <DetailsStep
            formState={formState}
            formFields={page.formFields}
            onFieldChange={tracker.updateField}
            onAnswerChange={tracker.updateAnswer}
            onContinue={() => setStep('confirm')}
            onBack={handleChangeTime}
            serverErrors={tracker.submitValidation}
          />
        )}

        {step === 'confirm' && (
          <ReviewStep
            page={page}
            formState={formState}
            submitting={tracker.submitting}
            submitError={tracker.submitError}
            submitErrorIsConflict={tracker.submitErrorIsConflict}
            submitValidation={tracker.submitValidation}
            onConfirm={() => void handleConfirm()}
            onBack={() => setStep('details')}
            onEditTime={handleChangeTime}
            onEditDetails={() => setStep('details')}
          />
        )}

        {step === 'success' && tracker.confirmation && (
          <SuccessStep page={page} confirmation={tracker.confirmation} slot={selectedSlot} />
        )}
      </div>
    </PublicShell>
  );
}

/**
 * The booking sheet's own shape while it loads, rather than the word
 * "Loading..." on an otherwise empty screen.
 *
 * Shaped like the header and the two picker panels specifically so nothing
 * moves when the real content arrives - a skeleton that resolves into a
 * different layout is a flash of rearrangement, which is the thing a skeleton
 * exists to prevent.
 */
function BookingPageSkeleton() {
  return (
    <PublicShell>
      <div role="status" aria-label="Loading">
        <div className="h-4 w-32 animate-pulse rounded bg-gray-100" />
        <div className="mt-3 h-7 w-64 max-w-full animate-pulse rounded bg-gray-200" />
        <div className="mt-4 flex gap-5">
          <div className="h-4 w-24 animate-pulse rounded bg-gray-100" />
          <div className="h-4 w-28 animate-pulse rounded bg-gray-100" />
          <div className="h-4 w-40 animate-pulse rounded bg-gray-100" />
        </div>
        <div className="mt-10 grid gap-8 md:grid-cols-2">
          <div>
            <div className="mb-4 h-5 w-32 animate-pulse rounded bg-gray-200" />
            <div className="grid grid-cols-7 gap-1">
              {Array.from({ length: 42 }).map((_, i) => (
                <div key={i} className="h-10 animate-pulse rounded-md bg-gray-100 sm:h-11" />
              ))}
            </div>
          </div>
          <div>
            <div className="mb-4 h-5 w-36 animate-pulse rounded bg-gray-200" />
            <div className="grid grid-cols-3 gap-2 sm:grid-cols-4 md:grid-cols-3">
              {Array.from({ length: 8 }).map((_, i) => (
                <div key={i} className="h-11 animate-pulse rounded-lg bg-gray-100" />
              ))}
            </div>
          </div>
        </div>
      </div>
    </PublicShell>
  );
}
