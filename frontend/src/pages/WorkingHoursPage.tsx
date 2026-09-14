import { useCallback, useEffect, useMemo, useState } from 'react';
import { useLocation } from 'react-router-dom';
import { CopyPlus, Plus, Sparkles, X } from 'lucide-react';
import {
  BUTTON_GHOST, BUTTON_PRIMARY, BUTTON_SECONDARY, CARD, CHECKBOX, Field, FOCUS_RING,
  FORM_COLUMN, InlineNotice, INPUT, LoadError, PageHeader, Section, SkeletonCard
} from '../components/ui';
import { useAuth } from '../contexts/AuthContext';
import { useUnsavedChanges } from '../hooks/useUnsavedChanges';
import { NO_VALIDATION_ERRORS, api, errorMessage, validationErrors } from '../lib/api';
import type { ValidationErrors } from '../lib/api';
import { DAY_DISPLAY_ORDER, DAY_NAMES } from '../lib/dayOfWeek';
import { SAVED, unmappedSaveError } from '../lib/saveResult';
import type { SaveResult } from '../lib/saveResult';
import {
  DEFAULT_WORKDAY_INTERVAL, MONDAY, canCopyDay, copyDayToWeekdays, emptyWeek, toWeeklyHours,
} from '../lib/workingHours';
import type { WeeklyHours } from '../lib/workingHours';
import type { DayOfWeekNumber, TimeRangeDto } from '../lib/types';
import { useDocumentTitle } from '../lib/pageTitle';

/**
 * The one server validation key this screen shows beside its own input rather
 * than in the summary notice. `SaveWorkingScheduleCommandValidator` writes
 * "'Europe/Skopj' is not a recognized IANA time zone id." against it, which is
 * useless anywhere except under the box that was typed into.
 *
 * Per-interval failures (`Days[3].Intervals`) deliberately stay unmapped: the
 * key names a day by .NET's Sunday-zero index, and turning that into "the third
 * row of the Thursday cell" would be a second, drift-prone copy of a mapping
 * the server does not actually promise.
 */
const TIME_ZONE_FIELD = 'TimeZoneId';

const inputClass = INPUT;

export function WorkingHoursPage() {
  useDocumentTitle('Working hours');
  const { callProtected } = useAuth();
  const location = useLocation();
  const justCreatedPage = Boolean((location.state as { justCreatedPage?: boolean } | null)?.justCreatedPage);
  const [timeZoneId, setTimeZoneId] = useState(Intl.DateTimeFormat().resolvedOptions().timeZone);
  const [days, setDays] = useState<WeeklyHours>(emptyWeek());
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [result, setResult] = useState<SaveResult>(null);
  const [serverErrors, setServerErrors] = useState<ValidationErrors>(NO_VALIDATION_ERRORS);

  // Everything on this screen that is persisted, and nothing that is not: the
  // load/save flags, the notice and the server's field errors are all transient
  // UI and must never read as an unsaved edit.
  const draft = useMemo(() => ({ timeZoneId, days }), [timeZoneId, days]);
  const { markSaved, prompt } = useUnsavedChanges(draft);

  /**
   * A failed load must never fall through to the editor here, and this is the
   * screen where that matters most: `emptyWeek()` is seven closed days, so an
   * unreachable API would have presented a week that looks deliberately blank -
   * and saving it would close a schedule the organizer never touched.
   */
  const load = useCallback(() => {
    setLoading(true);
    setLoadError(null);
    callProtected((token) => api.availability.getSchedule(token))
      .then((schedule) => {
        if (schedule) {
          setTimeZoneId(schedule.timeZoneId);
          setDays(toWeeklyHours(schedule));
        }
        // Whatever is on screen once the load settles IS the persisted state -
        // including the untouched placeholder week an organizer with no
        // schedule yet gets. Called in the same callback as the two updates
        // above; the baseline is taken from the render they produce.
        markSaved();
      })
      .catch((e) => setLoadError(errorMessage(e, 'Could not load your working hours.')))
      .finally(() => setLoading(false));
  }, [callProtected, markSaved]);

  useEffect(() => {
    load();
  }, [load]);

  const toggleDay = (day: DayOfWeekNumber) => {
    setDays((prev) => ({ ...prev, [day]: { ...prev[day], isEnabled: !prev[day].isEnabled } }));
  };

  const addInterval = (day: DayOfWeekNumber) => {
    setDays((prev) => ({
      ...prev,
      [day]: { ...prev[day], intervals: [...prev[day].intervals, { ...DEFAULT_WORKDAY_INTERVAL }] },
    }));
  };

  const copyMondayToWeekdays = () => setDays((prev) => copyDayToWeekdays(prev, MONDAY));

  const updateInterval = (day: DayOfWeekNumber, index: number, field: keyof TimeRangeDto, value: string) => {
    setDays((prev) => {
      const intervals = prev[day].intervals.map((interval, i) =>
        i === index ? { ...interval, [field]: value.length === 5 ? `${value}:00` : value } : interval,
      );
      return { ...prev, [day]: { ...prev[day], intervals } };
    });
  };

  const removeInterval = (day: DayOfWeekNumber, index: number) => {
    setDays((prev) => ({
      ...prev,
      [day]: { ...prev[day], intervals: prev[day].intervals.filter((_, i) => i !== index) },
    }));
  };

  /**
   * Saving keeps the organizer here and says so.
   *
   * It used to `navigate` to the booking page's session list on success - the
   * only settings screen in the app that did - so the one confirmation an
   * organizer got that their whole week had saved was the screen disappearing.
   * Nothing on the destination mentioned the schedule, which made a successful
   * save and a silent no-op look identical, and it also threw away the state
   * needed to keep editing (the common case after a first pass at a week).
   *
   * Same-page settings get the same inline notice everywhere else uses; the
   * organizer navigates when they are finished, from the rail that is already
   * on screen.
   */
  const save = useCallback(async () => {
    setSaving(true);
    setResult(null);
    setServerErrors(NO_VALIDATION_ERRORS);
    try {
      const saved = await callProtected((token) =>
        api.availability.saveSchedule(token, timeZoneId, Object.values(days)),
      );
      // Re-seeded from the response rather than left as typed: the server
      // orders and normalises what it stores, so this is the state a reload
      // would show.
      setTimeZoneId(saved.timeZoneId);
      setDays(toWeeklyHours(saved));
      setResult(SAVED);
      markSaved();
    } catch (err) {
      // No `markSaved` here on purpose: a rejected save leaves the week exactly
      // as typed and still unsaved, so the page must stay dirty and keep
      // warning.
      setServerErrors(validationErrors(err));
      setResult(unmappedSaveError(err, [TIME_ZONE_FIELD], 'Failed to save schedule.'));
    } finally {
      setSaving(false);
    }
  }, [callProtected, timeZoneId, days, markSaved]);

  // Switched on, but with nothing in it. The schedule saves happily and the day
  // then produces no slots at all, which is indistinguishable on this screen
  // from a day that is working correctly - the row simply shows an "Add
  // interval" button where the times would be.
  const enabledWithoutHours = DAY_DISPLAY_ORDER.filter(
    (day) => days[day].isEnabled && days[day].intervals.length === 0,
  );
  const timeZoneError = serverErrors.for(TIME_ZONE_FIELD)[0];
  const canCopyMonday = canCopyDay(days, MONDAY);

  return (
    <div className={FORM_COLUMN}>
      <PageHeader
        title="Working hours"
        description="When you are open for bookings. This schedule applies to every booking page you own."
      />

      {/* The icon and the second line carried their own `text-blue-600` /
          `text-blue-700`, which is the notice's own tone written out again a
          shade lighter - so the sentence that says what to do next was fainter
          than the one merely announcing the page exists. `InlineNotice` owns the
          colour; the only thing worth stating here is the emphasis. */}
      {justCreatedPage && (
        <InlineNotice tone="info" className="mb-4">
          <Sparkles className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
          <span>
            <span className="font-medium">Your booking page is ready.</span>
            <span className="block">
              Set your working hours below so guests can actually book a time with you.
            </span>
          </span>
        </InlineNotice>
      )}

      {prompt}

      {loading ? (
        <SkeletonCard lines={6} />
      ) : loadError ? (
        <LoadError message={loadError} onRetry={load} />
      ) : (
        <div className="space-y-6">
          {/* The field this app's `<Field>` primitive was designed against:
              label, hint, and a server rejection that takes the hint's place
              while marking the box invalid. Three ids and two ARIA attributes
              had to be kept in step by hand here; now none of them appear at
              the call site and this reads as the one thing it is. */}
          <Field
            className="max-w-xs"
            label="Time zone"
            hint="An IANA identifier, e.g. Europe/Skopje."
            error={timeZoneError}
          >
            {(control) => (
              <input
                {...control}
                className={`${inputClass} w-full`}
                value={timeZoneId}
                onChange={(e) => setTimeZoneId(e.target.value)}
                placeholder="Europe/Skopje"
              />
            )}
          </Field>

          {/* One bordered list with dividers rather than seven separate cards:
              these are seven rows of one table, and seven borders said they were
              seven unrelated things. */}
          <Section
            title="Weekly schedule"
            description="A day can hold several intervals — a lunch break is simply a gap between two of them."
            actions={
              /* One shortcut, not an editor. "The same every day I work" is the
                 shape most weeks have, and building it by hand is four
                 checkboxes and four rows of times. Tuesday-Friday only: opening
                 Saturday because Monday is open would be the button doing
                 something nobody asked for, and the weekend is precisely where
                 hours differ. Disabled when Monday has nothing to copy, so it
                 can never quietly close the rest of the week. */
              <button
                type="button"
                onClick={copyMondayToWeekdays}
                disabled={!canCopyMonday}
                title={
                  canCopyMonday
                    ? 'Applies Monday’s hours to Tuesday, Wednesday, Thursday and Friday.'
                    : 'Set Monday’s hours first.'
                }
                className={BUTTON_SECONDARY}
              >
                <CopyPlus className="h-4 w-4" aria-hidden="true" />
                Copy Monday to weekdays
              </button>
            }
          >
            <ul className={`${CARD} divide-y divide-gray-100`}>
              {DAY_DISPLAY_ORDER.map((day) => {
                const config = days[day];
                return (
                  /* `px-3` below `sm`: a phone cannot spend 40px of a 327px
                     row on card padding when the thing inside it is two time
                     inputs that need 125px each to render "09:00 AM" without
                     clipping the M. Same trade the guest wizard's calendar
                     already makes at this width, and it reverts at `sm`. */
                  <li key={day} className="px-3 py-3.5 sm:px-5">
                    <div className="flex flex-wrap items-start gap-x-4 gap-y-2">
                      <label className="flex w-36 shrink-0 cursor-pointer items-center gap-3 py-1.5 text-[15px] text-gray-900">
                        <input type="checkbox" className={CHECKBOX} checked={config.isEnabled} onChange={() => toggleDay(day)} />
                        <span className={config.isEnabled ? 'font-medium' : ''}>{DAY_NAMES[day]}</span>
                      </label>

                      {!config.isEnabled ? (
                        <span className="py-1.5 text-[15px] text-gray-500">Closed</span>
                      ) : (
                        <div className="min-w-0 space-y-1.5">
                          {config.intervals.map((interval, index) => (
                            <div key={index} className="flex items-center gap-1 sm:gap-1.5">
                              {/*
                                A time input is ~125px at its natural width, and
                                the pair plus "to" and the remove button come to
                                ~305px. Below `sm` the day label has already
                                wrapped onto its own line, leaving 285px inside
                                the card - so the row bled 19px past its padding,
                                the remove button sat on the card's border, and by
                                320px the page itself scrolled sideways.

                                The `px-3` above buys back enough for both to
                                render at their full 125px at 375px and 390px, so
                                nothing is clipped; `flex-1 min-w-0` is what keeps
                                that graceful further down (88px each at 320px)
                                instead of overflowing, and `sm:flex-none` hands
                                them their natural width back at the first
                                breakpoint where the whole row fits beside the
                                label. The desktop layout is unchanged.

                                Note this is on the *inputs* and deliberately not
                                on the wrapper above: giving that a flex-basis of
                                0 stops it wrapping below the label at all, so it
                                shares the line with the 144px label instead and
                                squeezes these to 35px. Measured, not guessed.
                              */}
                              <input
                                type="time"
                                aria-label={`${DAY_NAMES[day]} interval ${index + 1} start`}
                                className={`${inputClass} min-w-0 flex-1 sm:flex-none`}
                                value={interval.start.slice(0, 5)}
                                onChange={(e) => updateInterval(day, index, 'start', e.target.value)}
                              />
                              <span className="shrink-0 text-sm text-gray-500">to</span>
                              <input
                                type="time"
                                aria-label={`${DAY_NAMES[day]} interval ${index + 1} end`}
                                className={`${inputClass} min-w-0 flex-1 sm:flex-none`}
                                value={interval.end.slice(0, 5)}
                                onChange={(e) => updateInterval(day, index, 'end', e.target.value)}
                              />
                              <button
                                type="button"
                                onClick={() => removeInterval(day, index)}
                                aria-label={`Remove ${DAY_NAMES[day]} interval ${index + 1}`}
                                className={`shrink-0 rounded p-1 text-gray-400 transition-colors hover:bg-red-50 hover:text-red-600 ${FOCUS_RING}`}
                              >
                                <X className="h-4 w-4" aria-hidden="true" />
                              </button>
                            </div>
                          ))}
                          <button type="button" onClick={() => addInterval(day)} className={`${BUTTON_GHOST} -ml-2`}>
                            <Plus className="h-3.5 w-3.5" aria-hidden="true" />
                            Add interval
                          </button>
                          {/* Where a disabled day says "Closed", an enabled one
                              with no intervals said nothing at all. It is the
                              row itself that has to carry this - the notice
                              below names the days, but only this says which
                              line of the table to look at. */}
                          {config.intervals.length === 0 && (
                            <p className="text-[13px] text-amber-700">No hours set — nothing can be booked.</p>
                          )}
                        </div>
                      )}
                    </div>
                  </li>
                );
              })}
            </ul>
          </Section>

          {enabledWithoutHours.length > 0 && (
            <InlineNotice tone="warning">
              {enabledWithoutHours.length === 1
                ? `${DAY_NAMES[enabledWithoutHours[0]]} is switched on but has no hours, so guests cannot book it.`
                : `${enabledWithoutHours.map((d) => DAY_NAMES[d]).join(', ')} are switched on but have no hours, so guests cannot book them.`}
              {' '}Add an interval to each, or switch the day off.
            </InlineNotice>
          )}

          {result && <InlineNotice tone={result.tone}>{result.message}</InlineNotice>}

          <div>
            <button type="button" onClick={() => void save()} disabled={saving} className={BUTTON_PRIMARY}>
              {saving ? 'Saving…' : 'Save schedule'}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
