import { describe, expect, it } from 'vitest';
import { answeredFields, answersByFieldId, customFieldName, missingRequiredFields } from '../bookingForm';
import { bookingFormField } from '../../test/factories';

describe('customFieldName', () => {
  it('produces the field name the backend decodes', () => {
    // Mirrors Domain's BookingFieldNames.ForCustomField. If this drifts, answers
    // are still recorded as events but land on no field at all - so the exact
    // shape is pinned rather than asserted loosely.
    expect(customFieldName('7c9e6679-7425-40de-944b-e07fc1f90ae7')).toBe(
      'custom:7c9e6679-7425-40de-944b-e07fc1f90ae7',
    );
  });
});

describe('answersByFieldId', () => {
  it('keys answers for lookup', () => {
    const map = answersByFieldId([
      { fieldId: 'a', value: 'Acme' },
      { fieldId: 'b', value: 'Pricing' },
    ]);

    expect(map.get('a')).toBe('Acme');
    expect(map.get('b')).toBe('Pricing');
    expect(map.get('missing')).toBeUndefined();
  });
});

describe('answeredFields', () => {
  const company = bookingFormField({ id: 'a', label: 'Company', displayOrder: 1 });
  const topic = bookingFormField({ id: 'b', label: 'Topic', displayOrder: 0 });

  it('returns answers in the organizer display order, not the order they arrived', () => {
    const result = answeredFields(
      [company, topic],
      [
        { fieldId: 'a', value: 'Acme' },
        { fieldId: 'b', value: 'Pricing' },
      ],
    );

    expect(result.map((r) => r.label)).toEqual(['Topic', 'Company']);
  });

  it('labels each answer from its field', () => {
    const result = answeredFields([company], [{ fieldId: 'a', value: 'Acme' }]);

    expect(result).toEqual([{ fieldId: 'a', label: 'Company', value: 'Acme' }]);
  });

  it('omits fields that were never answered', () => {
    const result = answeredFields([company, topic], [{ fieldId: 'a', value: 'Acme' }]);

    expect(result.map((r) => r.label)).toEqual(['Company']);
  });

  it('omits a blank answer rather than showing an empty row', () => {
    const result = answeredFields([company], [{ fieldId: 'a', value: '   ' }]);

    expect(result).toEqual([]);
  });

  it('drops an answer whose field the organizer has since removed', () => {
    // The booking keeps the answer - removing a field never rewrites past
    // bookings - but with no field there is nothing left to caption it with.
    const result = answeredFields([company], [{ fieldId: 'deleted-field', value: 'Orphaned' }]);

    expect(result).toEqual([]);
  });
});

describe('missingRequiredFields', () => {
  const required = bookingFormField({ id: 'a', label: 'Company', isRequired: true });
  const optional = bookingFormField({ id: 'b', label: 'Topic', isRequired: false });

  it('reports a required field with no answer', () => {
    expect(missingRequiredFields([required, optional], {}).map((f) => f.label)).toEqual(['Company']);
  });

  it('treats a whitespace-only answer as missing, exactly as the server does', () => {
    expect(missingRequiredFields([required], { a: '   ' })).toHaveLength(1);
  });

  it('reports nothing once every required field is answered', () => {
    expect(missingRequiredFields([required, optional], { a: 'Acme' })).toEqual([]);
  });

  it('never reports an optional field', () => {
    expect(missingRequiredFields([optional], {})).toEqual([]);
  });
});
