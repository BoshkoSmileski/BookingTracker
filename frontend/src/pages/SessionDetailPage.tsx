import type { HubConnection } from '@microsoft/signalr';
import { useCallback, useEffect, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import { ArrowLeft } from 'lucide-react';
import { EventTimeline } from '../components/EventTimeline';
import { MeetingPanel } from '../components/MeetingPanel';
import { ReminderStatusList } from '../components/ReminderStatusList';
import { RescheduleFlow } from '../components/RescheduleFlow';
import { StatusBadge } from '../components/StatusBadge';
import {
  BUTTON_DANGER, BUTTON_GHOST, BUTTON_SECONDARY, CARD, ConfirmPanel, FieldLabel, FOCUS_RING,
  FORM_COLUMN, InlineNotice, INPUT, LoadError, META, PageHeader, Section, SkeletonLines
} from '../components/ui';
import { useAuth } from '../contexts/AuthContext';
import { api, errorMessage } from '../lib/api';
import { formatCalendarDate, formatCalendarTime } from '../lib/calendarDates';
import { formatDateTime } from '../lib/dates';
import { describeEvent } from '../lib/eventDisplay';
import { SESSION_STATUS_PARAM, parseSessionStatus, sessionListPath } from '../lib/sessionFilter';
import { createDashboardConnection } from '../lib/signalr';
import { answeredFields } from '../lib/bookingForm';
import { hasMeeting } from '../lib/meeting';
import { useDocumentTitle } from '../lib/pageTitle';
import type {
  AvailableSlotDto, BookingFormFieldDto, BookingReminderDto, BookingSessionDto, BookingSessionEventDto
} from '../lib/types';

export function SessionDetailPage() {
  useDocumentTitle('Session');
  const { pageId = '', sessionId = '' } = useParams<{ pageId: string; sessionId: string }>();
  const { callProtected, getAccessToken } = useAuth();
  const [searchParams] = useSearchParams();
  // Which list the organizer came from. Carried in the URL rather than router
  // state so Back still lands on the right tab after a reload or a shared link.
  const cameFrom = parseSessionStatus(searchParams.get(SESSION_STATUS_PARAM));
  const [session, setSession] = useState<BookingSessionDto | null>(null);
  const [events, setEvents] = useState<BookingSessionEventDto[]>([]);
  const [pageSlug, setPageSlug] = useState<string | null>(null);
  const [formFields, setFormFields] = useState<BookingFormFieldDto[]>([]);
  const [loading, setLoading] = useState(true);
  // `refresh` had no catch at all, so a failed load left the skeleton on screen
  // for ever - indistinguishable from a slow one.
  const [loadError, setLoadError] = useState<string | null>(null);

  const [showCancelForm, setShowCancelForm] = useState(false);
  const [cancelReason, setCancelReason] = useState('');
  const [showReschedule, setShowReschedule] = useState(false);
  const [actionBusy, setActionBusy] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const [actionMessage, setActionMessage] = useState<string | null>(null);

  const [emailHistory, setEmailHistory] = useState<BookingSessionEventDto[] | null>(null);
  const [emailHistoryLoading, setEmailHistoryLoading] = useState(false);
  const [emailHistoryError, setEmailHistoryError] = useState<string | null>(null);
  const [reminders, setReminders] = useState<BookingReminderDto[]>([]);

  const refresh = useCallback(async () => {
    // Reminders are fetched here rather than on demand so that cancelling or
    // rescheduling - both of which call refresh() - immediately reflects the
    // reminders those actions cancelled or regenerated.
    // The booking page itself rather than the whole list: it carries the slug
    // this screen already needed *and* the custom fields the answers below are
    // labelled from, in one request instead of every page the organizer owns.
    try {
      const [sessionDto, timeline, page, reminderList] = await callProtected((token) =>
        Promise.all([
          api.organizer.getSession(token, pageId, sessionId),
          api.organizer.getTimeline(token, pageId, sessionId),
          api.organizer.getBookingPage(token, pageId),
          api.organizer.getSessionReminders(token, pageId, sessionId),
        ]),
      );
      setSession(sessionDto);
      setEvents(timeline);
      setPageSlug(page.slug);
      setFormFields(page.formFields);
      setReminders(reminderList);
      setLoadError(null);
    } catch (e) {
      setLoadError(errorMessage(e, 'Could not load this session.'));
    } finally {
      setLoading(false);
    }
  }, [callProtected, pageId, sessionId]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  useEffect(() => {
    let connection: HubConnection | undefined = createDashboardConnection(getAccessToken);

    connection.on('SessionEventAdded', (e: BookingSessionEventDto) => {
      if (e.sessionId !== sessionId) return;
      setEvents((prev) => [...prev, e]);
    });
    connection.on('SessionUpdated', (s: BookingSessionDto) => {
      if (s.id !== sessionId) return;
      setSession(s);
    });

    connection
      .start()
      .then(() => connection?.invoke('JoinBookingPageGroup', pageId))
      .catch(() => {});

    return () => {
      void connection?.stop();
      connection = undefined;
    };
  }, [pageId, sessionId, getAccessToken]);

  /**
   * Every action starts from a clean slate. A previous success used to stay on
   * screen beside a new failure, so the page could show "Booking rescheduled"
   * and "Failed to cancel booking" at the same time.
   *
   * The wording below is deliberate: all three of these handlers **queue** an
   * email (`IEmailNotificationService` writes an `EmailNotifications` row;
   * `EmailQueueProcessor` sends it later, out of the request). Saying "sent" at
   * this moment is a claim the app cannot make - and the one place that can is
   * the email history below, which is built from `EmailSent` events written
   * only after a send actually succeeded.
   */
  const startAction = () => {
    setActionBusy(true);
    setActionError(null);
    setActionMessage(null);
  };

  const handleCancel = async () => {
    startAction();
    try {
      await callProtected((token) => api.organizer.cancelSession(token, pageId, sessionId, cancelReason || undefined));
      setShowCancelForm(false);
      setActionMessage('Booking cancelled. Notification emails are queued — check the email history for delivery.');
      await refresh();
    } catch (e) {
      setActionError(errorMessage(e, 'Failed to cancel booking.'));
    } finally {
      setActionBusy(false);
    }
  };

  const handleReschedule = async (slot: AvailableSlotDto) => {
    startAction();
    try {
      await callProtected((token) => api.organizer.rescheduleSession(token, pageId, sessionId, slot.localDate, slot.localStartTime));
      setShowReschedule(false);
      setActionMessage('Booking rescheduled. Confirmation emails are queued — check the email history for delivery.');
      await refresh();
    } catch (e) {
      setActionError(errorMessage(e, 'Failed to reschedule booking.'));
    } finally {
      setActionBusy(false);
    }
  };

  const handleResendConfirmation = async () => {
    startAction();
    try {
      await callProtected((token) => api.organizer.resendConfirmation(token, pageId, sessionId));
      setActionMessage('Confirmation email queued for resending — check the email history for delivery.');
    } catch (e) {
      setActionError(errorMessage(e, 'Failed to resend confirmation.'));
    } finally {
      setActionBusy(false);
    }
  };

  const toggleEmailHistory = async () => {
    if (emailHistory !== null) {
      setEmailHistory(null);
      return;
    }
    setEmailHistoryLoading(true);
    setEmailHistoryError(null);
    try {
      const result = await callProtected((token) => api.organizer.getEmailHistory(token, pageId, sessionId));
      setEmailHistory(result);
    } catch (e) {
      // Had a `finally` but no `catch`, so a failed load put the button back to
      // "View email history" and said nothing - which is indistinguishable from
      // a booking that has sent no emails, the opposite conclusion. Its own
      // state rather than `actionError` so it is reported in the Timeline
      // section, beside the button that failed.
      setEmailHistoryError(errorMessage(e, 'Could not load the email history.'));
    } finally {
      setEmailHistoryLoading(false);
    }
  };

  const canManage = session?.status === 'Submitted';
  const answers = answeredFields(formFields, session?.answers ?? []);

  return (
    <div className={FORM_COLUMN}>
      <Link
        to={sessionListPath(pageId, cameFrom)}
        className={`mb-4 inline-flex items-center gap-1.5 rounded text-sm text-gray-500 transition-colors hover:text-gray-900 ${FOCUS_RING}`}
      >
        <ArrowLeft className="h-4 w-4" aria-hidden="true" />
        Back to {cameFrom.toLowerCase()} sessions
      </Link>

      {loading && <SkeletonLines lines={4} />}

      {/* This screen's own treatment, now the app's - see `LoadError`. */}
      {!loading && loadError && (
        <LoadError message={loadError} onRetry={() => { setLoading(true); void refresh(); }} />
      )}

      {session && (
        <>
          {/* The page title was a hand-rolled 16px `h1` - one step below every
              section heading in the app and 8px below every other page title.
              PageHeader puts it back on the scale and gives the status the
              actions slot every other screen uses. */}
          <PageHeader
            title={session.name || 'Anonymous visitor'}
            description={session.email || 'No email captured yet'}
            actions={<StatusBadge status={session.status} />}
          />

          <div className="mb-8">
            <dl className="grid grid-cols-1 gap-x-8 text-sm sm:grid-cols-2">
              <Detail label="Email" value={session.email} />
              <Detail label="Phone" value={session.phone} />
              <Detail label="Date" value={session.selectedDate && formatCalendarDate(session.selectedDate)} />
              <Detail label="Time" value={session.selectedTime && formatCalendarTime(session.selectedTime)} />
            </dl>

            {canManage && (
              <div className="mt-4 flex flex-wrap gap-2">
                <button
                  type="button"
                  disabled={actionBusy}
                  onClick={() => { setShowReschedule((v) => !v); setShowCancelForm(false); }}
                  className={BUTTON_SECONDARY}
                >
                  Reschedule
                </button>
                <button
                  type="button"
                  disabled={actionBusy}
                  onClick={() => void handleResendConfirmation()}
                  className={BUTTON_SECONDARY}
                >
                  Resend confirmation
                </button>
                {/* Destructive action last and visually separate, so it is never
                    the button reached for by muscle memory. Hidden while its own
                    confirmation is open: the panel's confirm button carries the
                    same words, and two buttons with one accessible name on
                    screen at once is ambiguous to anyone not looking at it. */}
                {!showCancelForm && (
                  <button
                    type="button"
                    disabled={actionBusy}
                    onClick={() => { setShowCancelForm(true); setShowReschedule(false); }}
                    className={`${BUTTON_DANGER} sm:ml-auto`}
                  >
                    Cancel booking
                  </button>
                )}
              </div>
            )}

            {actionMessage && <InlineNotice tone="success" className="mt-3">{actionMessage}</InlineNotice>}
            {actionError && <InlineNotice tone="error" className="mt-3">{actionError}</InlineNotice>}

            {showCancelForm && (
              // The app's one confirmation shape, shared with deleting a booking
              // page and disconnecting a calendar - and the reason it is a panel
              // rather than a dialog is exactly this: the reason field belongs
              // inside the question.
              <div className="mt-3">
                <ConfirmPanel
                  title="Cancel this booking?"
                  confirmLabel="Cancel booking"
                  busyLabel="Cancelling…"
                  cancelLabel="Keep booking"
                  busy={actionBusy}
                  onConfirm={() => void handleCancel()}
                  onCancel={() => setShowCancelForm(false)}
                >
                  <p>
                    The guest and you are both emailed, the slot is released, and any pending
                    reminders are cancelled. This cannot be undone.
                  </p>
                  {/* "shared with the guest" is domain context, not part of
                      the optional marker, so it moves out of the parenthesis and
                      onto the control as a description - which keeps it in the
                      accessible name chain via aria-describedby rather than
                      losing it to a standardised label. */}
                  <FieldLabel htmlFor="cancel-reason" optional className="mt-3 text-red-900">
                    Reason
                  </FieldLabel>
                  <textarea
                    id="cancel-reason"
                    rows={2}
                    aria-describedby="cancel-reason-hint"
                    className={`${INPUT} w-full`}
                    value={cancelReason}
                    onChange={(e) => setCancelReason(e.target.value)}
                  />
                  <p id="cancel-reason-hint" className="mt-1.5 text-[13px] text-red-900">
                    Shared with the guest in the cancellation email.
                  </p>
                </ConfirmPanel>
              </div>
            )}

            {showReschedule && pageSlug && (
              <div className={`mt-3 ${CARD} p-4`}>
                {/* No timeZoneId: the organizer is the person whose clock these
                    times are on, so a "your time" second reading would restate
                    the number beside it. */}
                <RescheduleFlow
                  slug={pageSlug}
                  submitting={actionBusy}
                  onConfirm={(slot) => void handleReschedule(slot)}
                  onCancel={() => setShowReschedule(false)}
                />
              </div>
            )}
          </div>
        </>
      )}

      <div className="space-y-8">
        {session && hasMeeting(session) && (
          // Above Answers: on the day, this is the only thing on the screen
          // anyone needs. MeetingPanel renders nothing without a link, but the
          // Section wrapper would still draw a heading, so the check is here.
          <Section title="Meeting">
            <MeetingPanel meetingProvider={session.meetingProvider} meetingUrl={session.meetingUrl} />
          </Section>
        )}

        {answers.length > 0 && (
          // Its own section rather than another row in the grid above: those
          // four are the same on every booking, whereas these are whatever this
          // organizer asked for, and an answer can be a paragraph.
          <Section title="Answers">
            <dl className={`${CARD} divide-y divide-gray-100`}>
              {answers.map((answer) => (
                <div key={answer.fieldId} className="px-5 py-3.5">
                  <dt className={META}>{answer.label}</dt>
                  <dd className="mt-1 text-[15px] break-words whitespace-pre-wrap text-gray-900">{answer.value}</dd>
                </div>
              ))}
            </dl>
          </Section>
        )}

        {reminders.length > 0 && (
          <Section title="Reminders">
            <ReminderStatusList reminders={reminders} />
          </Section>
        )}

        <Section
          title="Timeline"
          actions={
            <button type="button" onClick={() => void toggleEmailHistory()} className={BUTTON_GHOST}>
              {emailHistory !== null ? 'Hide email history' : emailHistoryLoading ? 'Loading…' : 'View email history'}
            </button>
          }
        >
          {emailHistoryError && (
            <InlineNotice tone="error" className="mb-4">{emailHistoryError}</InlineNotice>
          )}

          {emailHistory !== null && (
            // Built from `EmailSent`/`ReminderSent` events, which
            // EmailQueueProcessor writes only *after* a send succeeded - so this
            // is the one surface on the screen that may say "sent" at all, and
            // an empty one genuinely means nothing has gone out yet.
            <div className={`mb-4 ${CARD} px-4 py-3`}>
              {emailHistory.length === 0 ? (
                <p className={META}>Nothing delivered yet. Emails appear here once they have actually been sent.</p>
              ) : (
                <ul className="divide-y divide-gray-200 text-sm">
                  {emailHistory.map((e) => (
                    <li key={e.id} className="flex justify-between gap-2 py-1.5 first:pt-0 last:pb-0">
                      <span className="text-gray-900">{describeEvent(e)}</span>
                      <span className={`shrink-0 tabular-nums ${META}`}>{formatDateTime(e.timestamp)}</span>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          )}

          <EventTimeline events={events} />
        </Section>
      </div>
    </div>
  );
}

/**
 * One read-only label/value row. Named `Detail` to match the identical-in-
 * purpose component on `CalendarIntegrationPage`, and because `Field` is now
 * the shared *form* primitive in `components/ui` - two different things under
 * one name on one screen is how the next reader picks the wrong one.
 *
 * Deliberately not extracted alongside that one: the two lay out differently
 * on purpose (label column here, `justify-between` there - see the comment
 * below), so sharing them would mean a variant prop for a six-line component.
 */
function Detail({ label, value }: { label: string; value?: string | null }) {
  return (
    // Label column then value, not label-left/value-right: pushed apart across
    // a 384px column these four short values read as a price list, with the eye
    // having to cross empty space to pair each one up.
    <div className="flex gap-3 border-b border-gray-100 py-1.5">
      <dt className="w-14 shrink-0 text-gray-500">{label}</dt>
      <dd className="min-w-0 truncate text-gray-900">{value || '—'}</dd>
    </div>
  );
}
