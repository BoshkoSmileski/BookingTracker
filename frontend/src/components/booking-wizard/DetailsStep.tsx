import { useId } from 'react';
import type { BookingFormState } from '../../hooks/useBookingSessionTracker';
import { NO_VALIDATION_ERRORS } from '../../lib/api';
import type { ValidationErrors } from '../../lib/api';
import { customFieldName, missingRequiredFields } from '../../lib/bookingForm';
import { CUSTOM_ANSWER_MAX_LENGTH } from '../../lib/config';
import type { BookingFormFieldDto } from '../../lib/types';
import { BUTTON_PRIMARY, BUTTON_SECONDARY, Field, INPUT, SECTION_HEADING } from '../ui';

interface DetailsStepProps {
  formState: BookingFormState;
  /** The organizer's custom questions, in display order. Empty for a page that asks none. */
  formFields: BookingFormFieldDto[];
  onFieldChange: (field: 'name' | 'email' | 'phone' | 'message', value: string) => void;
  onAnswerChange: (fieldId: string, value: string) => void;
  onContinue: () => void;
  onBack: () => void;
  /**
   * Field messages from a submit the server rejected. The confirm step is where
   * the guest pressed the button, but the inputs are here - so this is what lets
   * "Company is required." appear under Company rather than as two abstract
   * words a step away from anything they can act on.
   */
  serverErrors?: ValidationErrors;
}

const inputClass = `${INPUT} w-full`;

/**
 * Step two: who is booking.
 *
 * Laid out in two columns from `sm` up. At the shell's 768px measure a single
 * column of four stacked inputs left most of the sheet empty and made a
 * four-field form look like a long one - pairing name with email and phone with
 * nothing halves its apparent length without hiding anything.
 *
 * The organizer's own questions are the same controls in the same grid, under
 * their own heading rather than in a panel of their own. To the guest they are
 * simply more of the form; boxing them off would announce an internal
 * distinction (built-in field vs. custom field) that means nothing to the
 * person filling it in. The heading exists only because they are the
 * organizer's questions rather than the ones every booking asks, and it is
 * omitted entirely when there are none.
 */
export function DetailsStep({
  formState,
  formFields,
  onFieldChange,
  onAnswerChange,
  onContinue,
  onBack,
  serverErrors = NO_VALIDATION_ERRORS,
}: DetailsStepProps) {
  const questionsHeadingId = useId();

  // Required custom fields gate Continue exactly as name/email already do, so a
  // visitor is stopped here rather than at the Confirm button a step later. The
  // server re-checks at submit regardless - this is convenience, not the
  // enforcement point.
  const missingRequired = missingRequiredFields(formFields, formState.answers);
  const missingCore = [
    ...(formState.name.trim() === '' ? ['Name'] : []),
    ...(formState.email.trim() === '' ? ['Email'] : []),
  ];
  const missing = [...missingCore, ...missingRequired.map((f) => f.label)];
  const canContinue = missing.length === 0;

  return (
    <div>
      <h2 className={`${SECTION_HEADING} mb-1`}>Your details</h2>
      <p className="mb-6 text-sm text-gray-500">
        The organizer uses this to confirm the booking and reach you about it.
      </p>

      <div className="grid gap-x-5 gap-y-5 sm:grid-cols-2">
        <Field label="Name">
          {(control) => (
            <input
              {...control}
              className={inputClass}
              value={formState.name}
              onChange={(e) => onFieldChange('name', e.target.value)}
              placeholder="Jane Doe"
              autoComplete="name"
              autoFocus
              required
              aria-required="true"
            />
          )}
        </Field>

        {/* The hint says why the field is being asked for. A guest handing
            over an email address is entitled to know what it is for. */}
        <Field label="Email" hint="Your confirmation and any reminders go here.">
          {(control) => (
            <input
              {...control}
              type="email"
              className={inputClass}
              value={formState.email}
              onChange={(e) => onFieldChange('email', e.target.value)}
              placeholder="jane@example.com"
              autoComplete="email"
              required
              aria-required="true"
            />
          )}
        </Field>

        <Field label="Phone" optional>
          {(control) => (
            <input
              {...control}
              type="tel"
              className={inputClass}
              value={formState.phone}
              onChange={(e) => onFieldChange('phone', e.target.value)}
              placeholder="+1 555 123 4567"
              autoComplete="tel"
            />
          )}
        </Field>

        <Field label="Anything to share beforehand" optional className="sm:col-span-2">
          {(control) => (
            <textarea
              {...control}
              className={inputClass}
              rows={3}
              value={formState.message}
              onChange={(e) => onFieldChange('message', e.target.value)}
              placeholder="What would you like to cover?"
            />
          )}
        </Field>
      </div>

      {formFields.length > 0 && (
        <section aria-labelledby={questionsHeadingId} className="mt-8 border-t border-gray-200 pt-6">
          <h2 id={questionsHeadingId} className={`${SECTION_HEADING} mb-5`}>
            A few more questions
          </h2>
          <div className="grid gap-x-5 gap-y-5 sm:grid-cols-2">
            {formFields.map((field) => {
              const value = formState.answers[field.id] ?? '';
              // A long answer gets the full measure; a short one pairs up, so
              // the questions read as part of the same form above them.
              const isLong = field.type === 'LongText';
              // The server keys these by the same `custom:{id}` string the
              // tracker reports answers under, so no mapping table is needed -
              // the one encoder is the one lookup.
              const [serverError] = serverErrors.for(customFieldName(field.id));

              // The branch `<Field>`'s render prop was written for: only the
              // caller knows whether the control is the textarea or the input,
              // so only the caller can place the id and the ARIA on it.
              return (
                <Field
                  key={field.id}
                  label={field.label}
                  optional={!field.isRequired}
                  error={serverError}
                  className={isLong ? 'sm:col-span-2' : undefined}
                >
                  {(control) => (isLong ? (
                    <textarea
                      {...control}
                      className={inputClass}
                      rows={3}
                      value={value}
                      maxLength={CUSTOM_ANSWER_MAX_LENGTH}
                      onChange={(e) => onAnswerChange(field.id, e.target.value)}
                      required={field.isRequired}
                      aria-required={field.isRequired}
                    />
                  ) : (
                    <input
                      {...control}
                      className={inputClass}
                      value={value}
                      maxLength={CUSTOM_ANSWER_MAX_LENGTH}
                      onChange={(e) => onAnswerChange(field.id, e.target.value)}
                      required={field.isRequired}
                      aria-required={field.isRequired}
                    />
                  ))}
                </Field>
              );
            })}
          </div>
        </section>
      )}

      <div className="mt-8 flex flex-col-reverse gap-3 border-t border-gray-200 pt-6 sm:flex-row sm:items-center sm:justify-between">
        <button type="button" onClick={onBack} className={BUTTON_SECONDARY}>
          Back
        </button>
        <div className="sm:text-right">
          <button
            type="button"
            onClick={onContinue}
            disabled={!canContinue}
            className={`${BUTTON_PRIMARY} w-full sm:w-auto`}
          >
            Continue
          </button>
          {!canContinue && (
            // Names the fields that are actually missing rather than restating
            // the rule: with custom questions in play, "Name and email are
            // required" could be shown while both are filled in. This is the
            // only thing on screen explaining why Continue is disabled, so it
            // is announced rather than left as silent grey text.
            <p role="status" className="mt-2 text-[13px] text-gray-500">
              Still needed: {missing.join(', ')}.
            </p>
          )}
        </div>
      </div>
    </div>
  );
}
