import type { BookingFormFieldDto, BookingSessionAnswerDto } from './types';

/**
 * The custom-field half of the booking form, as pure functions.
 *
 * `customFieldName` mirrors Domain's `BookingFieldNames.ForCustomField` - an
 * answer is reported as an ordinary `FieldChanged` event whose `fieldName` is
 * `custom:{fieldId}`, which is what let custom questions be added without a new
 * `BookingEventType`. The two sides of that convention must agree exactly, so
 * this is the only place the frontend builds one, the same way `lib/config.ts`
 * mirrors `BookingFieldNames` for the four built-in fields.
 */
const CUSTOM_FIELD_PREFIX = 'custom:';

export function customFieldName(fieldId: string): string {
  return `${CUSTOM_FIELD_PREFIX}${fieldId}`;
}

/** Answers keyed by field id, for joining against a page's `formFields`. */
export function answersByFieldId(answers: BookingSessionAnswerDto[]): Map<string, string> {
  return new Map(answers.map((a) => [a.fieldId, a.value]));
}

export interface AnsweredField {
  fieldId: string;
  label: string;
  value: string;
}

/**
 * A booking's answers in the organizer's display order, labelled.
 *
 * Answers to a field the organizer has since deleted are dropped rather than
 * shown label-less: the booking keeps them (removing a field never rewrites
 * past bookings), but there is no longer anything to caption them with. This
 * matches what the confirmation email does with the same data.
 */
export function answeredFields(
  fields: BookingFormFieldDto[],
  answers: BookingSessionAnswerDto[],
): AnsweredField[] {
  const byId = answersByFieldId(answers);
  return [...fields]
    .sort((a, b) => a.displayOrder - b.displayOrder)
    .map((field) => ({ fieldId: field.id, label: field.label, value: byId.get(field.id) ?? '' }))
    .filter((entry) => entry.value.trim() !== '');
}

/** Required fields still blank - what disables Continue on the details step. */
export function missingRequiredFields(
  fields: BookingFormFieldDto[],
  answers: Record<string, string>,
): BookingFormFieldDto[] {
  return fields.filter((field) => field.isRequired && (answers[field.id] ?? '').trim() === '');
}
