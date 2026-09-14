import { useCallback, useEffect, useState } from 'react';
import type { FormEvent, ReactNode } from 'react';
import { Link, useParams } from 'react-router-dom';
import {
  BUTTON_PRIMARY, FOCUS_RING, FORM_COLUMN, InlineNotice, LoadError, META, PageHeader, SkeletonLines,
} from '../components/ui';
import type { NoticeTone } from '../components/ui';
import { useAuth } from '../contexts/AuthContext';
import { useUnsavedChanges } from '../hooks/useUnsavedChanges';
import { api, errorMessage } from '../lib/api';
import { MEETING_PROVIDER_OPTIONS } from '../lib/meeting';
import { SAVED, saveFailed } from '../lib/saveResult';
import type { SaveResult } from '../lib/saveResult';
import type { CalendarConnectionDto, MeetingProvider } from '../lib/types';
import { useDocumentTitle } from '../lib/pageTitle';

/**
 * What a Google Meet page will actually do, given the organizer's calendar.
 *
 * The screen previously showed one fixed sentence - "this needs a connected
 * calendar" - whether or not there was one, so the two states an organizer most
 * needs to tell apart looked identical: a page that will produce join links and
 * a page that silently will not. A Meet link is a side effect of exporting the
 * booking to Google Calendar, so it takes a connection, a healthy one, and
 * booking export switched on; any of the three missing means bookings still
 * succeed and simply have no meeting.
 *
 * Read-only reasoning over what the calendar endpoint already returns - no new
 * backend, and nothing here decides anything the sync service does not.
 */
function meetReadiness(connection: CalendarConnectionDto | null): {
  tone: NoticeTone;
  message: ReactNode;
  linkLabel: string;
} {
  if (!connection) {
    return {
      tone: 'warning',
      message:
        'Google Calendar is not connected, so bookings on this page will have no meeting link. They will still be taken, confirmed and emailed — just without a Join button.',
      linkLabel: 'Connect Google Calendar',
    };
  }

  if (connection.status !== 'Connected') {
    return {
      tone: 'warning',
      message: `Your Google Calendar connection needs attention (${connection.healthStatus}), so new bookings on this page may get no meeting link.`,
      linkLabel: 'Fix the connection',
    };
  }

  if (!connection.exportBookings) {
    return {
      tone: 'warning',
      message:
        'Your calendar is connected, but "Export bookings" is switched off. Google Meet links are created on the calendar event, so with no event there is no link.',
      linkLabel: 'Turn on booking export',
    };
  }

  return {
    tone: 'success',
    message: `Ready. New bookings on this page get a Google Meet link automatically, created on "${connection.externalCalendarName}" (${connection.externalAccountEmail}) and included in the confirmation email, the calendar invitation and the reminders.`,
    linkLabel: 'Calendar settings',
  };
}

/**
 * How bookings on this page meet. Its own screen rather than a field on
 * Details, because it is the natural home for the providers this is built to
 * grow into: a Custom link needs a URL field beside the choice, and Zoom or
 * Teams would each need their own connect flow. One radio group today, with
 * somewhere for those to land.
 */
export function MeetingSettingsPage() {
  useDocumentTitle('Meeting type');
  const { pageId = '' } = useParams<{ pageId: string }>();
  const { callProtected } = useAuth();
  const [provider, setProvider] = useState<MeetingProvider>('None');
  const [connection, setConnection] = useState<CalendarConnectionDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [result, setResult] = useState<SaveResult>(null);

  // Only the radio group is persisted; the calendar connection beside it is
  // read-only context this screen reasons over and can never be edited here.
  const { markSaved, prompt } = useUnsavedChanges(provider);

  // Both together: the meeting type is only half the answer, and the screen
  // would otherwise have to state a consequence it cannot check. Which is also
  // why neither may be allowed to fail silently - `meetReadiness` reads a null
  // connection as "not connected", so a failed calendar request would have the
  // screen assert an outcome it never actually looked up.
  const load = useCallback(() => {
    setLoading(true);
    setLoadError(null);
    Promise.all([
      callProtected((token) => api.organizer.getBookingPage(token, pageId)),
      callProtected((token) => api.calendar.getConnection(token)),
    ])
      .then(([page, calendarConnection]) => {
        setProvider(page.meetingProvider);
        setConnection(calendarConnection);
        markSaved();
      })
      .catch((e) => setLoadError(errorMessage(e, 'Could not load your meeting settings.')))
      .finally(() => setLoading(false));
  }, [callProtected, pageId, markSaved]);

  useEffect(() => {
    load();
  }, [load]);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setSaving(true);
    setResult(null);
    try {
      await callProtected((token) => api.organizer.updateBookingPageMeetingSettings(token, pageId, provider));
      setResult(SAVED);
      markSaved();
    } catch (err) {
      setResult(saveFailed(err));
    } finally {
      setSaving(false);
    }
  };

  const readiness = meetReadiness(connection);

  return (
    <div className={FORM_COLUMN}>
      <PageHeader
        title="Meeting"
        description="How guests meet you when they book this page."
      />

      {prompt}

      {loading ? (
        <SkeletonLines lines={4} />
      ) : loadError ? (
        <LoadError message={loadError} onRetry={load} />
      ) : (
        <form onSubmit={handleSubmit} className="space-y-6">
          <fieldset className="space-y-2">
            {/* A legend rather than a floating label, so the group has a real
                accessible name instead of the radios being announced alone. */}
            <legend className="sr-only">Meeting type</legend>
            {MEETING_PROVIDER_OPTIONS.map((option) => (
              <label
                key={option.value}
                className={`flex cursor-pointer gap-3 rounded-lg border p-4 transition-colors ${
                  provider === option.value
                    ? 'border-accent-600 bg-accent-50'
                    : 'border-gray-200 hover:border-gray-300'
                }`}
              >
                <input
                  type="radio"
                  name="meeting-provider"
                  value={option.value}
                  checked={provider === option.value}
                  onChange={() => setProvider(option.value)}
                  className={`mt-0.5 h-[18px] w-[18px] shrink-0 ${FOCUS_RING}`}
                />
                <span className="min-w-0">
                  <span className="block text-[15px] font-medium text-gray-900">{option.label}</span>
                  <span className={`mt-0.5 block ${META}`}>{option.description}</span>
                </span>
              </label>
            ))}
          </fieldset>

          {provider === 'GoogleMeet' && (
            // Stated up front rather than discovered as a missing link on the
            // first booking, and stated as it actually is rather than as a
            // standing caveat - see meetReadiness.
            <InlineNotice tone={readiness.tone}>
              <span>
                {readiness.message}{' '}
                <Link
                  to={`/dashboard/${pageId}/settings/calendar`}
                  className={`rounded font-medium underline ${FOCUS_RING}`}
                >
                  {readiness.linkLabel}
                </Link>
              </span>
            </InlineNotice>
          )}

          <p className={META}>
            This applies to new bookings. Meetings already created keep their links, so changing
            this never breaks a link a guest is already holding.
          </p>

          {result && <InlineNotice tone={result.tone}>{result.message}</InlineNotice>}

          <button type="submit" disabled={saving} className={BUTTON_PRIMARY}>
            {saving ? 'Saving…' : 'Save'}
          </button>
        </form>
      )}
    </div>
  );
}
