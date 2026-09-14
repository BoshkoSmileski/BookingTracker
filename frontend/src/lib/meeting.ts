import type { MeetingProvider } from './types';

/**
 * The frontend's single place for turning a booking's meeting into words and
 * deciding whether there is one to join.
 *
 * A pure helper in `lib/` rather than a snippet repeated on each screen, for
 * the same reason `bookingForm.ts` and `reminders.ts` are: four surfaces show
 * this (the organizer's session detail, the guest's manage page, the wizard's
 * success step, and the organizer's meeting settings screen), and a label that
 * says "Google Meet" on one and "Meet" on another is the kind of drift nobody
 * notices until a guest asks which one is right.
 *
 * Mirrors EmailNotificationService.DescribeMeeting and
 * CalendarInvitationGenerator.DescribeMeeting on the backend - the same
 * hand-synced mirroring `lib/types.ts` does for DTOs, and the reason
 * MEETING_PROVIDER_LABELS is keyed by the enum name rather than by a
 * separately-invented id.
 */
export const MEETING_PROVIDER_LABELS: Record<MeetingProvider, string> = {
  None: 'In person',
  GoogleMeet: 'Google Meet',
};

/** How the organizer's settings screen lists the choices, in the order they are offered. */
export const MEETING_PROVIDER_OPTIONS: {
  value: MeetingProvider;
  label: string;
  description: string;
}[] = [
  {
    value: 'None',
    label: MEETING_PROVIDER_LABELS.None,
    description: 'No meeting link. Guests meet you wherever your booking page says.',
  },
  {
    value: 'GoogleMeet',
    label: MEETING_PROVIDER_LABELS.GoogleMeet,
    description:
      'Google creates a Meet link for every booking and adds it to the calendar event, the confirmation email, the reminders and the calendar invitation.',
  },
];

/** Human label for a provider. Falls back to the raw value so an enum this build predates still renders as something. */
export function meetingProviderLabel(provider: MeetingProvider | null | undefined): string | null {
  if (!provider || provider === 'None') return null;
  return MEETING_PROVIDER_LABELS[provider] ?? provider;
}

/**
 * Whether a booking actually has a meeting to join.
 *
 * Both halves are required on purpose. `meetingProvider` without
 * `meetingUrl` is a real and expected state - the booking page asked for a
 * Meet, but the organizer had no connected calendar, or Google was
 * unreachable when the booking was made - and in that state there is nothing
 * to link to, so the UI must show no button rather than a dead one.
 */
export function hasMeeting(booking: {
  meetingProvider: MeetingProvider | null;
  meetingUrl: string | null;
}): boolean {
  return Boolean(booking.meetingUrl) && booking.meetingProvider !== null && booking.meetingProvider !== 'None';
}
