import { describe, expect, it } from 'vitest';
import { sessionEvent } from '../../test/factories';
import { describeEvent, formatEventValue, hasValueDiff, isBookingCancelledOrRescheduled } from '../eventDisplay';

describe('eventDisplay', () => {
  describe('describeEvent', () => {
    it('describes lifecycle events in plain language', () => {
      expect(describeEvent(sessionEvent({ eventType: 'SessionStarted' }))).toBe('Booking page opened');
      expect(describeEvent(sessionEvent({ eventType: 'BookingSubmitted' }))).toBe('Booking submitted');
      expect(describeEvent(sessionEvent({ eventType: 'BookingAbandoned' }))).toBe('Booking abandoned');
      expect(describeEvent(sessionEvent({ eventType: 'BookingRescheduled' }))).toBe('Booking rescheduled');
      expect(describeEvent(sessionEvent({ eventType: 'BrowserClosed' }))).toBe('Browser closed');
    });

    it('distinguishes a field being set from a field being cleared', () => {
      expect(describeEvent(sessionEvent({ eventType: 'FieldChanged', fieldName: 'Email', newValue: 'a@b.c' })))
        .toBe('Email changed');
      expect(describeEvent(sessionEvent({ eventType: 'FieldChanged', fieldName: 'Email', newValue: null })))
        .toBe('Email deleted');
    });

    it('distinguishes a first selection from a change', () => {
      expect(describeEvent(sessionEvent({ eventType: 'DateSelected', oldValue: null }))).toBe('Date selected');
      expect(describeEvent(sessionEvent({ eventType: 'DateSelected', oldValue: '2026-08-01' }))).toBe('Date changed');
      expect(describeEvent(sessionEvent({ eventType: 'TimeSelected', oldValue: null }))).toBe('Time selected');
      expect(describeEvent(sessionEvent({ eventType: 'TimeSelected', oldValue: '09:00' }))).toBe('Time changed');
    });

    it('names who cancelled and includes the reason when present', () => {
      expect(describeEvent(sessionEvent({ eventType: 'BookingCancelled', fieldName: 'Organizer', newValue: 'double booked' })))
        .toBe('Booking cancelled by organizer (double booked)');
      expect(describeEvent(sessionEvent({ eventType: 'BookingCancelled', fieldName: 'Customer', newValue: null })))
        .toBe('Booking cancelled by customer');
    });

    it('maps known email templates to readable labels', () => {
      expect(describeEvent(sessionEvent({ eventType: 'EmailSent', fieldName: 'BookingConfirmation', newValue: 'jane@example.com' })))
        .toBe('Confirmation email sent to jane@example.com');
      expect(describeEvent(sessionEvent({ eventType: 'EmailSent', fieldName: 'OrganizerCancellationNotice', newValue: 'o@example.com' })))
        .toBe('Organizer cancellation notice sent to o@example.com');
    });

    it('falls back to a generic label for an unmapped email template', () => {
      // New notification types are added backend-side; the timeline must degrade
      // gracefully rather than render "undefined".
      expect(describeEvent(sessionEvent({ eventType: 'EmailSent', fieldName: 'SomeNewTemplate', newValue: 'x@y.z' })))
        .toBe('Email sent to x@y.z');
    });

    it('renders a reminder with its window key', () => {
      expect(describeEvent(sessionEvent({ eventType: 'ReminderSent', fieldName: '24h', newValue: 'jane@example.com' })))
        .toBe('24h reminder sent to jane@example.com');
    });

    it('falls back to the raw event type for anything unrecognised', () => {
      expect(describeEvent(sessionEvent({ eventType: 'SomethingBrandNew' }))).toBe('SomethingBrandNew');
    });
  });

  describe('hasValueDiff', () => {
    it('is true only for events that carry an old/new value pair', () => {
      expect(hasValueDiff(sessionEvent({ eventType: 'FieldChanged' }))).toBe(true);
      expect(hasValueDiff(sessionEvent({ eventType: 'DateSelected' }))).toBe(true);
      expect(hasValueDiff(sessionEvent({ eventType: 'TimeSelected' }))).toBe(true);
      expect(hasValueDiff(sessionEvent({ eventType: 'SessionStarted' }))).toBe(false);
      expect(hasValueDiff(sessionEvent({ eventType: 'EmailSent' }))).toBe(false);
    });
  });

  describe('formatEventValue', () => {
    it('trims a backend TimeOnly to HH:mm', () => {
      // TimeOnly serializes as "14:30:00.0000000".
      expect(formatEventValue(sessionEvent({ fieldName: 'Time' }), '14:30:00.0000000')).toBe('14:30');
    });

    it('leaves other field values untouched', () => {
      expect(formatEventValue(sessionEvent({ fieldName: 'Email' }), 'jane@example.com')).toBe('jane@example.com');
      expect(formatEventValue(sessionEvent({ fieldName: 'Date' }), '2026-08-20')).toBe('2026-08-20');
    });

    it('renders a null value explicitly rather than as an empty cell', () => {
      expect(formatEventValue(sessionEvent({ fieldName: 'Email' }), null)).toBe('NULL');
    });
  });

  describe('isBookingCancelledOrRescheduled', () => {
    it('flags only the two lifecycle changes that alter an existing booking', () => {
      expect(isBookingCancelledOrRescheduled(sessionEvent({ eventType: 'BookingCancelled' }))).toBe(true);
      expect(isBookingCancelledOrRescheduled(sessionEvent({ eventType: 'BookingRescheduled' }))).toBe(true);
      expect(isBookingCancelledOrRescheduled(sessionEvent({ eventType: 'BookingSubmitted' }))).toBe(false);
    });
  });
});
