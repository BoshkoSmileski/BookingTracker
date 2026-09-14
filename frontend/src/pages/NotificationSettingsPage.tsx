import { useCallback, useEffect, useId, useMemo, useState } from 'react';
import { CalendarClock } from 'lucide-react';
import {
  BUTTON_PRIMARY, CARD, CHECKBOX, FORM_COLUMN, InlineNotice, LoadError, META, PageHeader, Section,
  SkeletonLines,
} from '../components/ui';
import { useAuth } from '../contexts/AuthContext';
import { useUnsavedChanges } from '../hooks/useUnsavedChanges';
import { api, errorMessage } from '../lib/api';
import { saveFailed, saved } from '../lib/saveResult';
import type { SaveResult } from '../lib/saveResult';
import {
  MAX_REMINDER_INTERVALS,
  REMINDER_PRESETS,
  REMINDER_PREVIEW_CAPTION,
  buildReminderPreview,
  formatReminderLabel,
} from '../lib/reminders';
import type { NotificationSettingsDto } from '../lib/types';
import { useDocumentTitle } from '../lib/pageTitle';

const checkboxRowClass = 'flex cursor-pointer items-start gap-2.5 text-sm text-gray-700';

/** Settings -> Notifications. Organizer-wide, same as Working Hours / Calendar Integration. */
export function NotificationSettingsPage() {
  useDocumentTitle('Notifications');
  const { callProtected } = useAuth();
  const limitHintId = useId();

  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [notice, setNotice] = useState<SaveResult>(null);

  const [notifyGuest, setNotifyGuest] = useState(true);
  const [notifyOrganizer, setNotifyOrganizer] = useState(true);
  const [remindersEnabled, setRemindersEnabled] = useState(true);
  const [notifyOrganizerOnReminder, setNotifyOrganizerOnReminder] = useState(false);
  const [selectedMinutes, setSelectedMinutes] = useState<number[]>([1440]);

  // The five persisted toggles, and nothing derived: `options`, `customMinutes`
  // and `preview` are all computed from `selectedMinutes`, so including them
  // would only compare the same fact twice.
  const draft = useMemo(
    () => ({ notifyGuest, notifyOrganizer, remindersEnabled, notifyOrganizerOnReminder, selectedMinutes }),
    [notifyGuest, notifyOrganizer, remindersEnabled, notifyOrganizerOnReminder, selectedMinutes],
  );
  const { markSaved, prompt } = useUnsavedChanges(draft);

  const applySettings = useCallback((settings: NotificationSettingsDto) => {
    setNotifyGuest(settings.notifyGuestOnBooking);
    setNotifyOrganizer(settings.notifyOrganizerOnBooking);
    setRemindersEnabled(settings.remindersEnabled);
    setNotifyOrganizerOnReminder(settings.notifyOrganizerOnReminderSent);
    setSelectedMinutes(settings.reminderMinutesBeforeEvent);
  }, []);

  /**
   * The `cancelled` flag is what stops a late response writing to an unmounted
   * component; it is not error handling, and there was none - so a failed load
   * left the skeleton up permanently. Falling through to the form would have
   * been worse than on most screens: its state is initialised to the backend's
   * own defaults (everything on, one 24h reminder), so a failed load would have
   * shown settings that look real and saved over whatever the organizer had.
   */
  const load = useCallback(() => {
    let cancelled = false;
    setLoading(true);
    setLoadError(null);
    callProtected((token) => api.organizer.getNotificationSettings(token))
      .then((result) => {
        if (!cancelled) {
          applySettings(result);
          markSaved();
        }
      })
      .catch((e) => {
        if (!cancelled) setLoadError(errorMessage(e, 'Could not load your notification settings.'));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [callProtected, applySettings, markSaved]);

  useEffect(() => load(), [load]);

  // Any saved value that isn't one of the presets still needs a row, or toggling
  // something else would silently drop it on the next save.
  const customMinutes = useMemo(
    () => selectedMinutes.filter((m) => !REMINDER_PRESETS.some((p) => p.minutes === m)).sort((a, b) => a - b),
    [selectedMinutes],
  );

  const options = useMemo(
    () => [...REMINDER_PRESETS, ...customMinutes.map((m) => ({ minutes: m, label: formatReminderLabel(m) }))]
      .sort((a, b) => a.minutes - b.minutes),
    [customMinutes],
  );

  const preview = useMemo(() => buildReminderPreview(selectedMinutes), [selectedMinutes]);

  /**
   * The notice stays until the next save replaces it.
   *
   * It used to clear itself after five seconds, which is the wrong contract for
   * a settings form: the organizer is still on the page, may well have looked
   * away while it saved, and the thing being confirmed ("upcoming bookings have
   * been updated") is not something to glance at. A timer also took the *error*
   * away, which is worse - the one message you might need to act on was the one
   * that disappeared.
   *
   * A `SaveResult` rather than this screen's own `{ type, message }`: that
   * private shape was a second vocabulary for what every other settings form
   * already says - see `lib/saveResult.ts`.
   */

  const toggleMinutes = (minutes: number) => {
    setSelectedMinutes((current) =>
      current.includes(minutes) ? current.filter((m) => m !== minutes) : [...current, minutes].sort((a, b) => a - b),
    );
  };

  const atLimit = selectedMinutes.length >= MAX_REMINDER_INTERVALS;
  const noneSelected = remindersEnabled && selectedMinutes.length === 0;

  const handleSave = async () => {
    setSaving(true);
    setNotice(null);
    try {
      const payload: NotificationSettingsDto = {
        notifyGuestOnBooking: notifyGuest,
        notifyOrganizerOnBooking: notifyOrganizer,
        remindersEnabled,
        reminderMinutesBeforeEvent: [...selectedMinutes].sort((a, b) => a - b),
        notifyOrganizerOnReminderSent: notifyOrganizerOnReminder,
      };
      const result = await callProtected((token) => api.organizer.updateNotificationSettings(token, payload));
      applySettings(result);
      markSaved();
      setNotice(saved('Notification settings saved. Upcoming bookings have been updated.'));
    } catch (e) {
      // Through errorMessage (inside saveFailed) rather than a fixed string: the
      // reminder-interval rules ("Reminder intervals must be between ...
      // minutes.") are the only thing that explains a rejection here, and
      // swallowing them left the organizer with a form that refused to save and
      // would not say why.
      setNotice(saveFailed(e, 'Failed to save notification settings. Please try again.'));
    } finally {
      setSaving(false);
    }
  };

  // The header stays in both states, so the screen keeps its identity while it
  // loads and while it explains that it could not.
  if (loading || loadError) {
    return (
      <div className={FORM_COLUMN}>
        <PageHeader title="Notifications" />
        {loading
          ? <SkeletonLines lines={5} />
          : <LoadError message={loadError!} onRetry={() => { load(); }} />}
      </div>
    );
  }

  return (
    <div className={FORM_COLUMN}>
      <PageHeader
        title="Notifications"
        description="Which automatic emails go out for your bookings — confirmations, cancellations, reschedules, and reminders. These apply to every booking page you own."
      />

      {prompt}

      {notice && (
        <InlineNotice tone={notice.tone} className="mb-4">
          {notice.message}
        </InlineNotice>
      )}

      <div className="space-y-8">
        <Section title="Booking notifications">
          <div className="space-y-3">
          <label className={checkboxRowClass}>
            <input type="checkbox" className={`${CHECKBOX} mt-0.5`} checked={notifyGuest} onChange={(e) => setNotifyGuest(e.target.checked)} />
            <span>
              <span className="block font-medium text-gray-900">Guest notifications</span>
              <span className="block text-gray-500">
                Send the guest a confirmation, cancellation, and reschedule email for their own booking.
              </span>
            </span>
          </label>

          <label className={checkboxRowClass}>
            <input type="checkbox" className={`${CHECKBOX} mt-0.5`} checked={notifyOrganizer} onChange={(e) => setNotifyOrganizer(e.target.checked)} />
            <span>
              <span className="block font-medium text-gray-900">Organizer notifications</span>
              <span className="block text-gray-500">Email me when a booking is created, cancelled, or rescheduled.</span>
            </span>
          </label>
          </div>
        </Section>

        <Section title="Reminder emails">
          <label className={`${checkboxRowClass} mb-4`}>
            <input type="checkbox" className={`${CHECKBOX} mt-0.5`} checked={remindersEnabled} onChange={(e) => setRemindersEnabled(e.target.checked)} />
            <span>
              <span className="block font-medium text-gray-900">Send reminder emails to guests</span>
              <span className="block text-gray-500">Automatically remind guests before their appointment.</span>
            </span>
          </label>

          {remindersEnabled && (
            <>
              {/* A real group, not a layout box: this caption names the eight
                  tiles below it and the limit note under them applies to all
                  eight, but as bare paragraphs neither reached anyone using a
                  screen reader - each tile announced only its own "24 hours
                  before". `min-w-0` because a fieldset's UA default is
                  `min-width: min-content`, which the fragment it replaces did
                  not have. */}
              <fieldset className="min-w-0" aria-describedby={limitHintId}>
              <legend className="mb-2 text-sm font-medium text-gray-900">Send a reminder</legend>
              <div className="mb-1 grid grid-cols-2 gap-x-4 gap-y-2 sm:grid-cols-3">
                {options.map((option) => {
                  const checked = selectedMinutes.includes(option.minutes);
                  const blocked = !checked && atLimit;
                  return (
                    /*
                      A selected tile used to be `border-gray-900 bg-gray-50` -
                      near-black, from before the app had an accent - while the
                      checkbox inside it was already teal via `accent-color`. One
                      control, two colour languages, and the near-black read as
                      "disabled" next to the accented buttons on the same screen.

                      Focus lives on the label rather than only on the 18px box:
                      `has-[:focus-visible]` puts the app's ring around the whole
                      tile, so tabbing through eight of them is actually
                      followable. The input keeps its own ring for the case where
                      a browser does not support `:has()`.

                      Still a native checkbox in a native label - the disabled
                      state, the space key and the label association all come for
                      free, and the limit is enforced by `disabled` rather than by
                      a click handler that silently does nothing.
                    */
                    <label
                      key={option.minutes}
                      className={`flex items-center gap-2.5 rounded-lg border px-3 py-2 text-sm transition-colors has-[:focus-visible]:ring-2 has-[:focus-visible]:ring-accent-500 has-[:focus-visible]:ring-offset-2 ${
                        checked
                          ? 'border-accent-600 bg-accent-50 font-medium text-accent-900'
                          : 'border-gray-200 text-gray-700'
                      } ${
                        blocked
                          ? 'cursor-not-allowed border-dashed bg-gray-50 text-gray-500'
                          : 'cursor-pointer hover:border-gray-400'
                      }`}
                    >
                      <input
                        type="checkbox"
                        className={CHECKBOX}
                        checked={checked}
                        disabled={blocked}
                        onChange={() => toggleMinutes(option.minutes)}
                      />
                      <span>{option.label} before</span>
                    </label>
                  );
                })}
              </div>
              {/* Help text under a group of controls, so it follows the same
                  rule as a hint under one: `META`, not 12px, which is below the
                  smallest step on the type scale. */}
              <p id={limitHintId} className={`mb-4 ${META}`}>
                Up to {MAX_REMINDER_INTERVALS} reminders per booking.{atLimit ? ' Limit reached - uncheck one to choose another.' : ''}
              </p>
              </fieldset>

              {noneSelected && (
                <InlineNotice tone="warning" className="mb-4">
                  Pick at least one reminder time, or switch reminder emails off.
                </InlineNotice>
              )}

              <div className={`${CARD} px-4 py-3`}>
                <div className="mb-1.5 flex items-center gap-2">
                  <CalendarClock className="h-4 w-4 text-gray-400" aria-hidden="true" />
                  <p className="text-sm font-medium text-gray-900">Guests will receive reminders:</p>
                </div>
                {preview.length === 0 ? (
                  <p className="text-sm text-gray-500">No reminders selected.</p>
                ) : (
                  <ul className="space-y-1">
                    {preview.map((row) => (
                      <li key={row.label} className="flex items-baseline gap-2 text-sm text-gray-700">
                        <span aria-hidden="true" className="text-gray-400">&bull;</span>
                        <span>
                          <span className="font-medium text-gray-900">{row.when}</span>
                          <span className="text-gray-500"> &mdash; {row.label}</span>
                        </span>
                      </li>
                    ))}
                  </ul>
                )}
                <p className="mt-2 text-[13px] text-gray-500">{REMINDER_PREVIEW_CAPTION}</p>
              </div>

              <label className={`${checkboxRowClass} mt-4`}>
                <input
                  type="checkbox"
                  className={`${CHECKBOX} mt-0.5`}
                  checked={notifyOrganizerOnReminder}
                  onChange={(e) => setNotifyOrganizerOnReminder(e.target.checked)}
                />
                <span>
                  <span className="block font-medium text-gray-900">Copy me on reminder emails</span>
                  <span className="block text-gray-500">
                    Send me a notice whenever a guest reminder goes out. Off by default - this fires once per reminder, per booking.
                  </span>
                </span>
              </label>
            </>
          )}
        </Section>

        <div>
          <button
            type="button"
            onClick={() => void handleSave()}
            disabled={saving || noneSelected}
            className={BUTTON_PRIMARY}
          >
            {saving ? 'Saving…' : 'Save settings'}
          </button>
        </div>
      </div>
    </div>
  );
}
