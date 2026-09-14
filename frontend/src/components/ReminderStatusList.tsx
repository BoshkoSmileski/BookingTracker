import { formatDateTime } from '../lib/dates';
import { META, StatusPill } from './ui';
import type { StatusTone } from './ui';
import type { BookingReminderDto, BookingReminderStatus } from '../lib/types';

const STATUS_TONE: Record<BookingReminderStatus, StatusTone> = {
  Scheduled: 'info',
  Queued: 'warning',
  Sent: 'success',
  Failed: 'danger',
  Cancelled: 'neutral',
  Skipped: 'neutral',
};

const STATUS_LABEL: Record<BookingReminderStatus, string> = {
  Scheduled: 'Upcoming',
  Queued: 'Queued',
  Sent: 'Sent',
  Failed: 'Failed',
  Cancelled: 'Cancelled',
  Skipped: 'Skipped',
};

/**
 * Reminder status + history for one booking. The backend has already flattened
 * scheduling state and delivery state into `status`, so this renders one row
 * per reminder with no client-side reconciliation - see BookingReminderDto.
 */
export function ReminderStatusList({ reminders }: { reminders: BookingReminderDto[] }) {
  if (reminders.length === 0) {
    return (
      <p className="text-sm text-gray-500">
        No reminders for this booking - either reminders are switched off, or the booking was made too close to its start time.
      </p>
    );
  }

  return (
    // Divided rows rather than a bordered box per reminder: this list already
    // sits inside a bordered panel, and a border inside a border reads as two
    // levels of nesting where there is only one.
    <ul className="divide-y divide-gray-100">
      {reminders.map((reminder) => (
        <li key={reminder.id} className="py-2 first:pt-0 last:pb-0">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <span className="text-sm font-medium text-gray-900">{reminder.label} before</span>
            <StatusPill tone={STATUS_TONE[reminder.status]}>{STATUS_LABEL[reminder.status]}</StatusPill>
          </div>

          {/* The label is the quiet half and the value the loud one. These were
              the other way round: `dt` was gray-400 inside a gray-500 list, so
              every caption was lighter than the timestamp it captioned. */}
          <dl className={`mt-1 grid grid-cols-1 gap-x-4 gap-y-0.5 sm:grid-cols-2 ${META}`}>
            <div className="flex gap-1">
              <dt>Scheduled for</dt>
              <dd className="text-gray-900">{formatDateTime(reminder.scheduledForUtc)}</dd>
            </div>
            {reminder.sentAtUtc && (
              <div className="flex gap-1">
                <dt>Sent</dt>
                <dd className="text-gray-900">{formatDateTime(reminder.sentAtUtc)}</dd>
              </div>
            )}
            {reminder.queuedAtUtc && !reminder.sentAtUtc && (
              <div className="flex gap-1">
                <dt>Queued</dt>
                <dd className="text-gray-900">{formatDateTime(reminder.queuedAtUtc)}</dd>
              </div>
            )}
            {/* AttemptCount only counts FAILED attempts (EmailNotification.RecordFailedAttempt
                increments it; MarkSent does not), so a first-time success correctly shows nothing. */}
            {reminder.attemptCount > 0 && (
              <div className="flex gap-1">
                <dt>Retries</dt>
                <dd className="text-gray-900">{reminder.attemptCount}</dd>
              </div>
            )}
          </dl>

          {reminder.failureReason && (
            <p className="mt-1 text-xs text-red-600">Delivery error: {reminder.failureReason}</p>
          )}
          {reminder.resolutionReason && (
            <p className="mt-1 text-xs text-gray-500">{reminder.resolutionReason}</p>
          )}
        </li>
      ))}
    </ul>
  );
}
