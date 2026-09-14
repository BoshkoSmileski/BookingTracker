import type { ReactNode } from 'react';
import type { BookingFormState } from '../../hooks/useBookingSessionTracker';
import { NO_VALIDATION_ERRORS } from '../../lib/api';
import type { ValidationErrors } from '../../lib/api';
import { answeredFields, customFieldName } from '../../lib/bookingForm';
import {
  addMinutesToCalendarTime,
  formatCalendarDateLong,
  formatCalendarTime,
  formatCalendarTimeRange,
} from '../../lib/calendarDates';
import { MEETING_PROVIDER_LABELS } from '../../lib/meeting';
import { timeZoneLabel } from '../../lib/timezone';
import type { BookingPageDto } from '../../lib/types';
import { BUTTON_PRIMARY, BUTTON_SECONDARY, FOCUS_RING, InlineNotice, SECTION_HEADING } from '../ui';
import { BookingInstructions } from './BookingInstructions';

interface ReviewStepProps {
  page: BookingPageDto;
  formState: BookingFormState;
  submitting: boolean;
  submitError: string | null;
  /** True when the slot was taken while the guest was filling the form - offers the recovery action. */
  submitErrorIsConflict: boolean;
  /** Field messages from a rejected submit, so an answer the server refused can be pointed at. */
  submitValidation?: ValidationErrors;
  onConfirm: () => void;
  onBack: () => void;
  /** Jump straight back to the thing being corrected, rather than stepping back through the flow. */
  onEditTime: () => void;
  onEditDetails: () => void;
}

/**
 * Step three: everything about to be booked, and the button that books it.
 *
 * Two rows of a definition list per group, each group with its own **Edit**
 * link. The step used to offer one Back button to the details step, so
 * correcting a time from here meant Back, Back, re-pick, forward, forward -
 * which is exactly the moment a guest abandons.
 *
 * The list also states the meeting and the time zone, which it previously did
 * not: the time zone row named the *visitor's* zone beside a time on the
 * *organizer's* clock, which was worse than saying nothing.
 */
export function ReviewStep({
  page,
  formState,
  submitting,
  submitError,
  submitErrorIsConflict,
  submitValidation = NO_VALIDATION_ERRORS,
  onConfirm,
  onBack,
  onEditTime,
  onEditDetails,
}: ReviewStepProps) {
  // A rejected answer is shown beside its own input, which lives a step back -
  // so the notice here has to offer the way there, the same way a taken slot
  // offers the way back to the calendar. Without it the message names a field
  // the guest is not looking at.
  const hasAnswerError = page.formFields.some(
    (field) => submitValidation.for(customFieldName(field.id)).length > 0,
  );
  // Built from the same helper the organizer's session detail uses, so the two
  // never disagree about order or about which answers are worth showing.
  const answers = answeredFields(
    page.formFields,
    Object.entries(formState.answers).map(([fieldId, value]) => ({ fieldId, value })),
  );

  const endTime = formState.selectedTime
    ? addMinutesToCalendarTime(formState.selectedTime, page.durationMinutes)
    : null;

  return (
    <div>
      <h2 className={`${SECTION_HEADING} mb-1`}>Check and confirm</h2>
      <p className="mb-6 text-sm text-gray-500">Nothing is booked until you confirm.</p>

      {/* "Time" and "Details", matching StepProgress and the details step's own
          heading. These read "When" and "You", which is a second vocabulary for
          the same two steps - a guest who has just clicked past "Time" and
          "Details" then has to work out that "When" is the first one. The edit
          labels stay descriptive, because they are accessible names for a
          control rather than a step name. */}
      <ReviewGroup title="Time" onEdit={onEditTime} editLabel="the date and time">
        {formState.selectedDate && (
          <ReviewRow term="Date">{formatCalendarDateLong(formState.selectedDate)}</ReviewRow>
        )}
        {formState.selectedTime && endTime && (
          <ReviewRow term="Time">{formatCalendarTimeRange(formState.selectedTime, endTime)}</ReviewRow>
        )}
        <ReviewRow term="Time zone">{timeZoneLabel(page.timeZoneId)}</ReviewRow>
        <ReviewRow term="Where">{MEETING_PROVIDER_LABELS[page.meetingProvider] ?? 'In person'}</ReviewRow>
      </ReviewGroup>

      <ReviewGroup title="Details" onEdit={onEditDetails} editLabel="your details">
        <ReviewRow term="Name">{formState.name}</ReviewRow>
        <ReviewRow term="Email">{formState.email}</ReviewRow>
        {formState.phone.trim() !== '' && <ReviewRow term="Phone">{formState.phone}</ReviewRow>}
        {formState.message.trim() !== '' && <ReviewRow term="Message">{formState.message}</ReviewRow>}
        {/* Answers sit in the same list as the rest of the booking, not a panel
            of their own: to the visitor they are simply more of what they just
            filled in, and reviewing them means reading one list. */}
        {answers.map((answer) => (
          <ReviewRow key={answer.fieldId} term={answer.label}>
            {answer.value}
          </ReviewRow>
        ))}
      </ReviewGroup>

      {/* Repeated here on purpose: this is the "before they complete a booking"
          moment, and the schedule step may have been several minutes ago. */}
      <BookingInstructions instructions={page.instructions} heading="Before you confirm" className="mt-6" />

      {submitError && (
        <InlineNotice tone="error" className="mt-6">
          <div>
            <p>{submitError}</p>
            {/* A conflict is the one submit failure with an obvious next move,
                and the message already tells the guest to make it - so the
                screen has to let them, rather than leaving them on a dead end
                with a Back button that goes to the wrong place. */}
            {submitErrorIsConflict && (
              <button
                type="button"
                onClick={onEditTime}
                className={`mt-2 rounded font-medium underline underline-offset-2 ${FOCUS_RING}`}
              >
                Choose another time
              </button>
            )}
            {hasAnswerError && (
              <button
                type="button"
                onClick={onEditDetails}
                className={`mt-2 rounded font-medium underline underline-offset-2 ${FOCUS_RING}`}
              >
                Go back to your details
              </button>
            )}
          </div>
        </InlineNotice>
      )}

      <div className="mt-8 flex flex-col-reverse gap-3 border-t border-gray-200 pt-6 sm:flex-row sm:items-center sm:justify-between">
        <button type="button" onClick={onBack} disabled={submitting} className={BUTTON_SECONDARY}>
          Back
        </button>
        <button
          type="button"
          onClick={onConfirm}
          disabled={submitting}
          className={`${BUTTON_PRIMARY} w-full sm:w-auto`}
        >
          {submitting ? 'Confirming…' : 'Confirm booking'}
        </button>
      </div>
      <p className="mt-3 text-[13px] text-gray-500 sm:text-right">
        {formState.selectedDate && formState.selectedTime
          ? `Books ${formatCalendarDateLong(formState.selectedDate)} at ${formatCalendarTime(formState.selectedTime)} and emails you the details.`
          : 'Books your appointment and emails you the details.'}
      </p>
    </div>
  );
}

/**
 * One group of the summary, with the control that corrects it.
 *
 * A hairline and a heading rather than a card, per the design system's rule
 * that a border means "distinct object" - three bordered boxes stacked here
 * would make the last screen before booking the busiest one in the flow.
 */
function ReviewGroup({
  title,
  onEdit,
  editLabel,
  children,
}: {
  title: string;
  onEdit: () => void;
  /**
   * Completes the button's accessible name - "Edit the date and time" rather
   * than a bare "Edit", which is ambiguous on a page carrying two of them and
   * useless out of context, which is how a screen reader lists controls.
   * Supplied through `aria-label`; see the note on `BookingHeader`'s Change
   * button for why an `sr-only` span does not work here.
   */
  editLabel: string;
  children: ReactNode;
}) {
  return (
    <section className="border-t border-gray-200 py-4 first-of-type:border-t-0 first-of-type:pt-0">
      <div className="mb-2 flex items-baseline justify-between gap-4">
        <h3 className="text-[13px] font-semibold tracking-wide text-gray-500 uppercase">{title}</h3>
        <button
          type="button"
          onClick={onEdit}
          aria-label={`Edit ${editLabel}`}
          className={`rounded text-[13px] font-medium text-accent-700 underline underline-offset-2 transition-colors hover:text-accent-900 ${FOCUS_RING}`}
        >
          Edit
        </button>
      </div>
      <dl className="grid gap-x-6 gap-y-2 sm:grid-cols-[8rem_minmax(0,1fr)]">{children}</dl>
    </section>
  );
}

function ReviewRow({ term, children }: { term: string; children: ReactNode }) {
  return (
    <>
      <dt className="text-sm text-gray-500">{term}</dt>
      <dd className="mb-1 text-[15px] break-words whitespace-pre-wrap text-gray-900 sm:mb-0">{children}</dd>
    </>
  );
}
