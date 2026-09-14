import { useCallback, useEffect, useMemo, useState } from 'react';
import { useParams, useSearchParams } from 'react-router-dom';
import { CheckCircle2, Loader2, RefreshCw, TriangleAlert } from 'lucide-react';
import {
  BUTTON_PRIMARY, BUTTON_SECONDARY, CHECKBOX, ConfirmPanel, Field, FORM_COLUMN,
  InlineNotice, INPUT, LoadError, META, PageHeader, Section, SkeletonLines, StatusPill
} from '../components/ui';
import type { StatusTone } from '../components/ui';
import { useAuth } from '../contexts/AuthContext';
import { useUnsavedChanges } from '../hooks/useUnsavedChanges';
import { api, errorMessage } from '../lib/api';
import { formatDateTime } from '../lib/dates';
import { failed, saveFailed, saved } from '../lib/saveResult';
import type { SaveResult } from '../lib/saveResult';
import type { CalendarConnectionDto, CalendarEventVisibility, ExternalCalendarDto } from '../lib/types';
import { useDocumentTitle } from '../lib/pageTitle';

const inputClass = INPUT;
const primaryButtonClass = BUTTON_PRIMARY;
const secondaryButtonClass = BUTTON_SECONDARY;
const checkboxRowClass = 'flex cursor-pointer items-start gap-2.5 text-sm text-gray-700';

/** Replaces the fourth independent filled-pill implementation in the app. */
const STATUS_TONE: Record<string, StatusTone> = {
  Connected: 'success',
  ReauthorizationRequired: 'warning',
  CalendarNotFound: 'warning',
  Error: 'danger',
  NotConnected: 'neutral',
};

const CALENDAR_ERROR_MESSAGES: Record<string, string> = {
  invalid_state: 'That connection link expired or was invalid. Please try connecting again.',
  access_denied: 'Google authorization was cancelled - no changes were made.',
  missing_code: 'Google did not return an authorization code. Please try again.',
  api_not_enabled:
    'The Google Calendar API is not enabled for this Google Cloud project yet. In Google Cloud Console, go to APIs & Services → Library, search for "Google Calendar API", and click Enable. Then try connecting again.',
  connect_failed: 'Something went wrong finishing the connection. Please try again.',
};

// The value is what the backend stores and substitutes into; the label is
// what the organizer reads. Only the custom input, where the placeholder
// syntax is something they actually have to type, shows the braces.
const TITLE_FORMAT_PRESETS = [
  { value: '{Service Name} with {Guest Name}', label: 'Service name with guest name' },
  { value: '{Guest Name} – {Service Name}', label: 'Guest name – service name' },
  { value: '{Service Name}', label: 'Service name' },
  { value: 'Meeting with {Guest Name}', label: 'Meeting with guest name' },
];

function formatSyncTime(value: string | null): string {
  if (!value) return 'Never';
  return formatDateTime(value);
}

/** Settings -> Calendar Integration. Connection is organizer-wide (like Working Hours), reached from any of the organizer's booking pages. */
export function CalendarIntegrationPage() {
  useDocumentTitle('Calendar integration');
  const { pageId = '' } = useParams<{ pageId: string }>();
  const { callProtected } = useAuth();
  const [searchParams, setSearchParams] = useSearchParams();

  const [connection, setConnection] = useState<CalendarConnectionDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  // Whether the connection has ever been read successfully. `connection` cannot
  // answer that - null is a legitimate result meaning "nothing connected".
  const [loaded, setLoaded] = useState(false);
  const [busy, setBusy] = useState(false);
  const [syncing, setSyncing] = useState(false);
  const [calendars, setCalendars] = useState<ExternalCalendarDto[] | null>(null);
  const [selectedCalendarId, setSelectedCalendarId] = useState('');
  const [notice, setNotice] = useState<SaveResult>(null);
  const [confirmingDisconnect, setConfirmingDisconnect] = useState(false);

  // Settings form-local state, seeded from the connection once it loads.
  const [importBusyEvents, setImportBusyEvents] = useState(true);
  const [exportBookings, setExportBookings] = useState(true);
  const [autoDeleteCancelled, setAutoDeleteCancelled] = useState(true);
  const [autoUpdateRescheduled, setAutoUpdateRescheduled] = useState(true);
  const [reminderMinutes, setReminderMinutes] = useState('');
  const [visibility, setVisibility] = useState<CalendarEventVisibility>('default');
  const [titleFormat, setTitleFormat] = useState(TITLE_FORMAT_PRESETS[0].value);
  const [titleFormatIsCustom, setTitleFormatIsCustom] = useState(false);
  const [savingSettings, setSavingSettings] = useState(false);

  // The seven values "Save settings" actually sends. The calendar picker is
  // deliberately out: choosing a calendar is applied by its own button and the
  // dropdown is re-seeded whenever the list is refreshed, so including it would
  // warn about a selection nothing is holding.
  const draft = useMemo(
    () => ({
      importBusyEvents, exportBookings, autoDeleteCancelled, autoUpdateRescheduled,
      reminderMinutes, visibility, titleFormat,
    }),
    [
      importBusyEvents, exportBookings, autoDeleteCancelled, autoUpdateRescheduled,
      reminderMinutes, visibility, titleFormat,
    ],
  );
  const { markSaved, prompt } = useUnsavedChanges(draft);

  /**
   * The notice stays until the next action replaces it.
   *
   * It used to clear itself after six seconds. For "Calendar settings saved"
   * that is merely inconsistent with every other settings screen; for the
   * failure branch it was actively wrong, because a sync error - the one thing
   * on this page an organizer has to act on - would quietly remove itself while
   * they were reading the panel underneath it.
   *
   * It is a `SaveResult` rather than this screen's own `{ type, message }`
   * because that private shape was a second vocabulary for what every settings
   * form already says - see `lib/saveResult.ts`.
   */

  const applyConnection = useCallback((result: CalendarConnectionDto | null) => {
    setConnection(result);
    if (result) {
      setImportBusyEvents(result.importBusyEvents);
      setExportBookings(result.exportBookings);
      setAutoDeleteCancelled(result.autoDeleteCancelledBookings);
      setAutoUpdateRescheduled(result.autoUpdateRescheduledBookings);
      setReminderMinutes(result.defaultReminderMinutes === null ? '' : String(result.defaultReminderMinutes));
      setVisibility(result.eventVisibility);
      setTitleFormat(result.eventTitleFormat);
      setTitleFormatIsCustom(!TITLE_FORMAT_PRESETS.some((p) => p.value === result.eventTitleFormat));
    }
    // Every adoption of the server's connection is a new baseline, and this is
    // the only place one happens - the initial load, each refresh after an
    // action, and a successful settings save all come through here. Refreshing
    // already overwrote whatever was typed, so reporting the page as clean
    // afterwards is what is actually on screen rather than a concession.
    markSaved();
  }, [markSaved]);

  /**
   * A null connection is a real answer here - "no calendar connected yet" - so
   * a failed request must not produce one. Without a `catch` this screen never
   * rendered at all on a failure (the early return below is unconditional on
   * `loading`); with one that resolved to null it would have told an organizer
   * with a working connection that they had none.
   *
   * Called by every action on the page too, so it reports a failed reload
   * without discarding the connection already on screen.
   */
  const refresh = useCallback(async () => {
    try {
      const result = await callProtected((token) => api.calendar.getConnection(token));
      applyConnection(result);
      setLoaded(true);
      setLoadError(null);
    } catch (e) {
      setLoadError(errorMessage(e, 'Could not load your calendar connection.'));
    } finally {
      setLoading(false);
    }
  }, [callProtected, applyConnection]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const retryLoad = () => {
    setLoading(true);
    void refresh();
  };

  // Land here after the Google OAuth redirect - surface the outcome once, then clean the URL.
  useEffect(() => {
    const connected = searchParams.get('connected');
    const calendarError = searchParams.get('calendarError');
    if (connected === 'true') {
      setNotice(saved('Google Calendar connected.'));
      void refresh();
    } else if (calendarError) {
      // Not a caught error: Google's failure arrives as a code on the URL, and
      // the table above is where this app already has the words for it.
      setNotice(failed(CALENDAR_ERROR_MESSAGES[calendarError] ?? 'Something went wrong connecting your calendar.'));
    }
    if (connected || calendarError) setSearchParams({}, { replace: true });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const handleConnect = async () => {
    setBusy(true);
    try {
      const { authorizationUrl } = await callProtected((token) => api.calendar.getGoogleAuthorizeUrl(token, pageId));
      window.location.href = authorizationUrl;
    } catch (e) {
      setNotice(saveFailed(e, 'Could not start the Google connection. Please try again.'));
      setBusy(false);
    }
  };

  const handleDisconnect = async () => {
    setBusy(true);
    setConfirmingDisconnect(false);
    try {
      await callProtected((token) => api.calendar.disconnect(token));
      setCalendars(null);
      await refresh();
      setNotice(saved('Google Calendar disconnected.'));
    } catch (e) {
      setNotice(saveFailed(e, 'Failed to disconnect. Please try again.'));
    } finally {
      setBusy(false);
    }
  };

  const handleSyncNow = async () => {
    setSyncing(true);
    try {
      const result = await callProtected((token) => api.calendar.syncNow(token));
      applyConnection(result);
      if (result.status === 'Connected') {
        setNotice(saved('Sync complete - your calendar connection is healthy.'));
      } else {
        // `syncNow` resolves even on failure - the outcome is on the DTO, so
        // there is no error to resolve here, only one to report.
        setNotice(failed(result.lastSyncError ?? `Sync finished with status: ${result.healthStatus}.`));
      }
    } catch (e) {
      setNotice(saveFailed(e, 'Sync Now failed unexpectedly. Please try again.'));
    } finally {
      setSyncing(false);
    }
  };

  const handleRefreshCalendars = async () => {
    setBusy(true);
    try {
      const result = await callProtected((token) => api.calendar.getCalendars(token));
      setCalendars(result);
      setSelectedCalendarId(connection?.externalCalendarId ?? result.find((c) => c.isPrimary)?.id ?? result[0]?.id ?? '');
    } catch (e) {
      setNotice(saveFailed(e, 'Failed to load your calendars. Please try again.'));
    } finally {
      setBusy(false);
    }
  };

  const handleChooseCalendar = async () => {
    const chosen = calendars?.find((c) => c.id === selectedCalendarId);
    if (!chosen) return;
    setBusy(true);
    try {
      const result = await callProtected((token) => api.calendar.selectCalendar(token, chosen.id, chosen.name));
      applyConnection(result);
      setNotice(saved(`Now syncing with "${chosen.name}".`));
    } catch (e) {
      setNotice(saveFailed(e, 'Failed to switch calendars. Please try again.'));
    } finally {
      setBusy(false);
    }
  };

  const handleSaveSettings = async () => {
    setSavingSettings(true);
    try {
      const [, eventResult] = await Promise.all([
        callProtected((token) => api.calendar.updateSyncSettings(token, importBusyEvents, exportBookings)),
        callProtected((token) =>
          api.calendar.updateEventSettings(
            token, titleFormat, autoDeleteCancelled, autoUpdateRescheduled,
            reminderMinutes.trim() === '' ? null : Number(reminderMinutes), visibility,
          ),
        ),
      ]);
      applyConnection(eventResult);
      setNotice(saved('Calendar settings saved.'));
    } catch (e) {
      // The only screen-reachable rejection here is the event-visibility rule,
      // and a fixed string threw it away along with everything else the server
      // might say.
      setNotice(saveFailed(e, 'Failed to save calendar settings. Please try again.'));
    } finally {
      setSavingSettings(false);
    }
  };

  // Nothing has ever loaded: the page has no connection state to render, so the
  // content area is the error rather than a "no calendar connected" panel that
  // would be a claim about the organizer's account rather than about the request.
  if (loading || (loadError && !loaded)) {
    return (
      <div className={FORM_COLUMN}>
        <PageHeader title="Calendar" />
        {loading ? <SkeletonLines lines={5} /> : <LoadError message={loadError!} onRetry={retryLoad} />}
      </div>
    );
  }

  const statusTone = connection ? (STATUS_TONE[connection.status] ?? STATUS_TONE.NotConnected) : STATUS_TONE.NotConnected;
  const statusLabel = connection ? connection.healthStatus : 'Not connected';

  return (
    <div className={FORM_COLUMN}>
      <PageHeader
        title="Calendar"
        description="Connect Google Calendar so your existing meetings block booking slots, and confirmed bookings appear on your calendar automatically. One connection covers every booking page you own."
        actions={<StatusPill tone={statusTone}>{statusLabel}</StatusPill>}
      />

      {/* A reload that failed while the connection below is still on screen:
          the data stays, and this says it may now be out of date. Distinct from
          the whole-page state above, which is for having no data at all. */}
      {prompt}

      {loadError && <LoadError message={loadError} onRetry={retryLoad} className="mb-4" />}

      {notice && (
        <InlineNotice tone={notice.tone} className="mb-4">
          {notice.tone === 'success'
            ? <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
            : <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />}
          <span>{notice.message}</span>
        </InlineNotice>
      )}

      <div className="space-y-8">
        <Section title="Connection">
          {connection ? (
            // Aligned label/value pairs are already a readable structure; a box
            // around six short values adds a rectangle and no information.
            <dl className="grid grid-cols-1 gap-x-8 text-sm sm:grid-cols-2">
              <Detail label="Provider" value={connection.provider} />
              <Detail label="Account" value={connection.externalAccountEmail} />
              <Detail label="Calendar" value={connection.externalCalendarName} />
              <Detail label="Synced bookings" value={String(connection.syncedBookingCount)} />
              <Detail label="Last success" value={formatSyncTime(connection.lastSuccessfulSyncAtUtc)} />
              <Detail label="Last failure" value={formatSyncTime(connection.lastFailedSyncAtUtc)} />
              {connection.lastSyncError && (
                <div className="pt-1 sm:col-span-2">
                  <dt className={META}>Most recent error</dt>
                  <dd className="text-sm text-red-600">{connection.lastSyncError}</dd>
                </div>
              )}
            </dl>
          ) : (
            /* The Meet half is the one an organizer can otherwise only discover
               from a booking that already happened without a link. */
            <p className="text-sm text-gray-500">
              No calendar connected yet. Your existing meetings do not block booking slots, and any
              booking page set to Google Meet takes bookings without a meeting link.
            </p>
          )}

          <div className="mt-4 flex flex-wrap gap-2">
            {!connection && (
              <button type="button" onClick={() => void handleConnect()} disabled={busy} className={primaryButtonClass}>
                {busy && <Loader2 className="mr-1.5 inline h-3.5 w-3.5 animate-spin" aria-hidden="true" />}
                {busy ? 'Connecting…' : 'Connect Google Calendar'}
              </button>
            )}
            {connection && (connection.status === 'ReauthorizationRequired' || connection.status === 'CalendarNotFound') && (
              <button type="button" onClick={() => void handleConnect()} disabled={busy} className={primaryButtonClass}>
                {busy ? 'Connecting…' : 'Reconnect Google Calendar'}
              </button>
            )}
            {connection && connection.status === 'Connected' && (
              <>
                <button type="button" onClick={() => void handleSyncNow()} disabled={syncing || busy} className={primaryButtonClass}>
                  {syncing && <Loader2 className="mr-1.5 inline h-3.5 w-3.5 animate-spin" aria-hidden="true" />}
                  {syncing ? 'Syncing…' : 'Sync Now'}
                </button>
                <button type="button" onClick={() => void handleConnect()} disabled={busy} className={secondaryButtonClass}>
                  Reconnect
                </button>
              </>
            )}
            {connection && !confirmingDisconnect && (
              <button
                type="button"
                onClick={() => setConfirmingDisconnect(true)}
                disabled={busy}
                className={secondaryButtonClass}
              >
                Disconnect
              </button>
            )}
          </div>

          {/*
            Inline, not `window.confirm`. The consequence here is three separate
            things and one of them - Google Meet links quietly no longer being
            created - is the whole reason to ask: a native dialog would run all
            three together into one unstyled paragraph, and several browsers
            truncate it. Links already issued are unaffected, because they
            belong to calendar events that already exist.
          */}
          {connection && confirmingDisconnect && (
            <div className="mt-4">
              <ConfirmPanel
                title="Disconnect Google Calendar?"
                confirmLabel="Disconnect"
                busyLabel="Disconnecting…"
                cancelLabel="Stay connected"
                busy={busy}
                onConfirm={() => void handleDisconnect()}
                onCancel={() => setConfirmingDisconnect(false)}
              >
                <ul className="list-disc space-y-1 pl-5">
                  <li>Existing bookings stay on your calendar, but new ones stop syncing to it.</li>
                  <li>Your Google Calendar stops blocking booking slots, so busy time becomes bookable.</li>
                  <li>
                    Any booking page set to Google Meet keeps taking bookings, but they get{' '}
                    <strong className="font-semibold">no meeting link</strong>. Links already sent to
                    guests keep working.
                  </li>
                </ul>
                <p className="mt-2">You can reconnect at any time.</p>
              </ConfirmPanel>
            </div>
          )}
        </Section>

        {connection && (
          <Section
            title="Which calendar"
            description="Busy events are read from this calendar, and confirmed bookings are added to it."
          >
            {/*
              A select and two buttons in one `flex-wrap` row: the select has no
              width of its own, so a long calendar name ("boshko.smileski@…
              — Work") pushed the row past its container before wrapping, and at
              320px the three items wrapped one-per-line in an order that put
              the action above the thing it acts on.

              Stacked by default and a row from `sm`, with the select given
              `min-w-0 flex-1` so a long option name truncates inside the
              control instead of widening it. Same conventions as the rest of
              the app; nothing new.
            */}
            <div className="flex flex-col gap-2 sm:flex-row sm:flex-wrap sm:items-center">
              <button
                type="button"
                onClick={() => void handleRefreshCalendars()}
                disabled={busy}
                className={`${secondaryButtonClass} w-full sm:w-auto`}
              >
                <span className="inline-flex items-center gap-1.5">
                  <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
                  Refresh Calendars
                </span>
              </button>

              {calendars && calendars.length > 0 && (
                <>
                  {/* Was an unlabelled select. Its `Section` heading says
                      "Which calendar", which a screen reader never associates
                      with the control - so it announced only its current value. */}
                  <label className="sr-only" htmlFor="calendar-picker">
                    Calendar to sync with
                  </label>
                  <select
                    id="calendar-picker"
                    className={`${inputClass} w-full min-w-0 sm:w-auto sm:flex-1`}
                    value={selectedCalendarId}
                    onChange={(e) => setSelectedCalendarId(e.target.value)}
                  >
                    {calendars.map((c) => (
                      <option key={c.id} value={c.id}>
                        {c.name}{c.isPrimary ? ' (Primary)' : ''}
                      </option>
                    ))}
                  </select>
                  <button
                    type="button"
                    onClick={() => void handleChooseCalendar()}
                    disabled={busy || !selectedCalendarId}
                    className={`${primaryButtonClass} w-full sm:w-auto`}
                  >
                    Use this calendar
                  </button>
                </>
              )}
            </div>

            {calendars && calendars.length === 0 && (
              <p className={`mt-2 ${META}`}>No calendars found on this Google account.</p>
            )}
          </Section>
        )}

        {connection && (
          <Section title="Sync behaviour">
            <div className="space-y-3">
              <label className={checkboxRowClass}>
                <input type="checkbox" className={`${CHECKBOX} mt-0.5`} checked={importBusyEvents} onChange={(e) => setImportBusyEvents(e.target.checked)} />
                <span>
                  <span className="block font-medium text-gray-900">Import busy events</span>
                  <span className="block text-gray-500">Existing events on your Google Calendar block matching booking slots.</span>
                </span>
              </label>

              <label className={checkboxRowClass}>
                <input type="checkbox" className={`${CHECKBOX} mt-0.5`} checked={exportBookings} onChange={(e) => setExportBookings(e.target.checked)} />
                <span>
                  <span className="block font-medium text-gray-900">Export bookings</span>
                  <span className="block text-gray-500">Confirmed bookings are added to your Google Calendar automatically.</span>
                </span>
              </label>

              <label className={checkboxRowClass}>
                <input type="checkbox" className={`${CHECKBOX} mt-0.5`} checked={autoDeleteCancelled} onChange={(e) => setAutoDeleteCancelled(e.target.checked)} />
                <span>
                  <span className="block font-medium text-gray-900">Automatically delete cancelled bookings</span>
                  <span className="block text-gray-500">If off, a cancelled booking's calendar event stays on your calendar.</span>
                </span>
              </label>

              <label className={checkboxRowClass}>
                <input type="checkbox" className={`${CHECKBOX} mt-0.5`} checked={autoUpdateRescheduled} onChange={(e) => setAutoUpdateRescheduled(e.target.checked)} />
                <span>
                  <span className="block font-medium text-gray-900">Automatically update rescheduled bookings</span>
                  <span className="block text-gray-500">If off, a rescheduled booking's calendar event stays at its original time.</span>
                </span>
              </label>

              <div className="grid grid-cols-1 gap-4 pt-2 sm:grid-cols-2">
                {/* The field `<Field>`'s render prop was written for: the
                    control is the <input>, and the "minutes before" unit sits
                    beside it in a flex row. A cloning API would have put the id
                    and the ARIA on that row - a label pointing at a non-control. */}
                <Field label="Default reminder">
                  {(control) => (
                    <div className="flex items-center gap-2">
                      <input
                        {...control}
                        type="number"
                        min={0}
                        className={`${inputClass} w-24`}
                        value={reminderMinutes}
                        onChange={(e) => setReminderMinutes(e.target.value)}
                        placeholder="Calendar default"
                      />
                      <span className="text-sm text-gray-500">minutes before</span>
                    </div>
                  )}
                </Field>

                <Field label="Event visibility">
                  {(control) => (
                    <select
                      {...control}
                      className={`${inputClass} w-full`}
                      value={visibility}
                      onChange={(e) => setVisibility(e.target.value as CalendarEventVisibility)}
                    >
                      <option value="default">Default</option>
                      <option value="public">Public</option>
                      <option value="private">Private</option>
                    </select>
                  )}
                </Field>
              </div>

              {/* Two controls, one hint. The select is *the* control, so it
                  takes the id; the custom input that appears beneath it keeps
                  its own `aria-label` and is handed the same
                  `aria-describedby`, which is the one part of the wiring that
                  may legitimately be shared. Its hint was `mt-1 text-xs` - 12px,
                  below the smallest step on the type scale - where every other
                  field hint in the app is `HELP_GAP` + `META`. */}
              <Field
                label="Event title format"
                hint={
                  titleFormatIsCustom
                    ? <>Use {'{Service Name}'} and {'{Guest Name}'} as placeholders.</>
                    : 'The title bookings get on your Google Calendar.'
                }
              >
                {(control) => (
                  <>
                    <select
                      {...control}
                      className={`${inputClass} w-full`}
                      value={titleFormatIsCustom ? 'custom' : titleFormat}
                      onChange={(e) => {
                        if (e.target.value === 'custom') {
                          setTitleFormatIsCustom(true);
                        } else {
                          setTitleFormatIsCustom(false);
                          setTitleFormat(e.target.value);
                        }
                      }}
                    >
                      {TITLE_FORMAT_PRESETS.map((preset) => (
                        <option key={preset.value} value={preset.value}>{preset.label}</option>
                      ))}
                      <option value="custom">Custom&hellip;</option>
                    </select>
                    {/* Was unlabelled - a placeholder is not a label, and it
                        disappears the moment anything is typed. */}
                    {titleFormatIsCustom && (
                      <input
                        className={`${inputClass} mt-2 w-full`}
                        value={titleFormat}
                        onChange={(e) => setTitleFormat(e.target.value)}
                        placeholder="{Service Name} with {Guest Name}"
                        aria-label="Custom event title format"
                        aria-describedby={control['aria-describedby']}
                      />
                    )}
                  </>
                )}
              </Field>
            </div>

            <button
              type="button"
              onClick={() => void handleSaveSettings()}
              disabled={savingSettings}
              className={`${primaryButtonClass} mt-4`}
            >
              {savingSettings ? 'Saving…' : 'Save settings'}
            </button>
          </Section>
        )}
      </div>
    </div>
  );
}

/** One label/value pair in the connection summary. */
function Detail({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex min-w-0 justify-between gap-3 border-b border-gray-100 py-1.5">
      <dt className="shrink-0 text-gray-500">{label}</dt>
      <dd className="truncate text-gray-900" title={value}>{value}</dd>
    </div>
  );
}
