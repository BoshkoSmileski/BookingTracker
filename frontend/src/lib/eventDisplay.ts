import { meetingProviderLabel } from './meeting';
import type { BookingSessionEventDto, MeetingProvider } from './types';

const EMAIL_TEMPLATE_LABELS: Record<string, string> = {
  BookingConfirmation: 'Confirmation email',
  OrganizerNotification: 'Organizer notification email',
  CancellationConfirmation: 'Cancellation email',
  OrganizerCancellationNotice: 'Organizer cancellation notice',
  RescheduleConfirmation: 'Reschedule confirmation email',
};

export function describeEvent(e: BookingSessionEventDto): string {
  switch (e.eventType) {
    case 'SessionStarted':
      return 'Booking page opened';
    case 'FieldChanged':
      return e.newValue ? `${e.fieldName} changed` : `${e.fieldName} deleted`;
    case 'DateSelected':
      return e.oldValue ? 'Date changed' : 'Date selected';
    case 'TimeSelected':
      return e.oldValue ? 'Time changed' : 'Time selected';
    case 'UserInactive':
      return 'User became inactive';
    case 'UserActive':
      return 'User became active again';
    case 'BrowserClosed':
      return 'Browser closed';
    case 'BookingSubmitted':
      return 'Booking submitted';
    case 'BookingAbandoned':
      return 'Booking abandoned';
    case 'BookingCancelled':
      return `Booking cancelled by ${e.fieldName === 'Organizer' ? 'organizer' : 'customer'}${e.newValue ? ` (${e.newValue})` : ''}`;
    case 'BookingRescheduled':
      return 'Booking rescheduled';
    case 'EmailSent':
      return `${EMAIL_TEMPLATE_LABELS[e.fieldName ?? ''] ?? 'Email'} sent to ${e.newValue}`;
    case 'ReminderSent':
      return `${e.fieldName} reminder sent to ${e.newValue}`;
    case 'MeetingLinkAssigned':
      // fieldName is the MeetingProviderType name; the URL in newValue is
      // deliberately not printed here - the timeline is a scannable list of
      // what happened, and the meeting has its own section on this screen
      // where the link is actually actionable.
      return `${meetingProviderLabel(e.fieldName as MeetingProvider) ?? 'Meeting'} link created`;
    default:
      return e.eventType;
  }
}

export function hasValueDiff(e: BookingSessionEventDto): boolean {
  return e.eventType === 'FieldChanged' || e.eventType === 'DateSelected' || e.eventType === 'TimeSelected';
}

/** Backend TimeOnly values serialize as "14:30:00.0000000" - trim to "14:30" for display. */
export function formatEventValue(e: BookingSessionEventDto, value: string | null): string {
  if (value === null) return 'NULL';
  if (e.fieldName === 'Time') return value.slice(0, 5);
  return value;
}

export function isBookingCancelledOrRescheduled(e: BookingSessionEventDto): boolean {
  return e.eventType === 'BookingCancelled' || e.eventType === 'BookingRescheduled';
}
