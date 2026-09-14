import { useCallback, useEffect, useMemo, useState } from 'react';
import type { FormEvent } from 'react';
import { CalendarRange, Plus, Trash2, X } from 'lucide-react';
import {
  BUTTON_DANGER, BUTTON_GHOST, BUTTON_PRIMARY, BUTTON_SECONDARY, CARD, CHECKBOX, ConfirmPanel,
  EmptyState, Field, FORM_COLUMN, InlineNotice, INPUT, LABEL, LoadError, META, PageHeader,
  Section, SkeletonLines, StatusPill
} from '../components/ui';
import { useAuth } from '../contexts/AuthContext';
import { useUnsavedChanges } from '../hooks/useUnsavedChanges';
import { api, errorMessage } from '../lib/api';
import { formatCalendarDate } from '../lib/calendarDates';
import { MAX_OVERRIDE_RANGES, toRangeInputs } from '../lib/availabilityOverrides';
import type { RangeInput } from '../lib/availabilityOverrides';
import {
  entryPeriod, entrySummary, isBlockedByWholeDay, mergeDateExceptions,
} from '../lib/dateExceptions';
import type { AvailabilityExceptionDto, AvailabilityExceptionType, AvailabilityOverrideDto } from '../lib/types';
import { useDocumentTitle } from '../lib/pageTitle';

const EXCEPTION_TYPES: AvailabilityExceptionType[] = ['Holiday', 'Vacation', 'Meeting', 'SickLeave', 'Training', 'Other'];

const DEFAULT_RANGE: RangeInput = { start: '09:00', end: '17:00' };

/** Which kind of exception the form is currently adding. */
type ExceptionKind = 'block' | 'hours';

/**
 * Everything about a date that does not follow the weekly working hours -
 * days off, and days with different hours - on one screen.
 *
 * These were two navigation destinations ("Blocked dates" and "Date
 * overrides") backed by two genuinely different domain concepts. The domain
 * split is correct and is untouched: an `AvailabilityException` subtracts time
 * and an `AvailabilityOverride` produces it, which the domain model keeps
 * apart on purpose. The split was wrong only as *navigation*: an organizer
 * planning "August 22 I work mornings, August 25-30 I am away" was answering
 * one question in two places, with no single list of what August actually
 * looks like.
 *
 * So the page is one form with an explicit choice of kind, and one list merged
 * in date order. Each kind still talks to its own endpoint, unchanged.
 */
export function DateExceptionsPage() {
  useDocumentTitle('Date exceptions');
  const { callProtected } = useAuth();
  const [exceptions, setExceptions] = useState<AvailabilityExceptionDto[]>([]);
  const [overrides, setOverrides] = useState<AvailabilityOverrideDto[]>([]);
  const [loading, setLoading] = useState(true);
  /** Whether both lists have ever been read. Having no exceptions is normal. */
  const [loaded, setLoaded] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [kind, setKind] = useState<ExceptionKind>('block');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  /**
   * Kept apart from the form's `error` because it is reported in a different
   * place: the form notice sits above the submit button, and a failed delete
   * shown there reads as a reason the form was rejected. This one renders at
   * the head of the list, beside the row that did not go away.
   */
  const [listError, setListError] = useState<string | null>(null);
  /**
   * The merged-list key of the entry whose deletion is being confirmed. Keyed
   * by `entry.key` rather than by row id because the list holds two kinds of
   * row from two different tables, whose ids are only unique within their own.
   */
  const [confirmingKey, setConfirmingKey] = useState<string | null>(null);
  const [deleting, setDeleting] = useState(false);

  // Blocked dates
  const [date, setDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [wholeDay, setWholeDay] = useState(true);
  const [startTime, setStartTime] = useState('09:00');
  const [endTime, setEndTime] = useState('17:00');
  const [type, setType] = useState<AvailabilityExceptionType>('Vacation');
  const [reason, setReason] = useState('');

  // Date-specific hours
  const [hoursDate, setHoursDate] = useState('');
  const [closed, setClosed] = useState(false);
  const [ranges, setRanges] = useState<RangeInput[]>([DEFAULT_RANGE]);
  const [note, setNote] = useState('');
  /** The date whose hours are being edited, or null when adding a new set. */
  const [editingDate, setEditingDate] = useState<string | null>(null);

  /**
   * Only an edit of hours that already exist is guarded, and `null` while the
   * form is a blank one is what scopes it.
   *
   * Both halves of this form are creation drafts the rest of the time - a
   * half-typed blocked period has no persisted state behind it to lose, and
   * warning about one would fire on every abandoned "actually, never mind".
   * Reopening an existing override is different: the fields were seeded from a
   * row on the server, and closing the page loses a real edit to it.
   */
  const draft = useMemo(
    () => (editingDate === null ? null : { hoursDate, closed, ranges, note }),
    [editingDate, hoursDate, closed, ranges, note],
  );
  const { markSaved, prompt } = useUnsavedChanges(draft);

  /**
   * Two endpoints because they are two resources; requested together because
   * they answer one question - and caught together for the same reason. Half a
   * list would be worse than none on this screen specifically: the merged view
   * is what makes a blocked date visibly outrank hours set on it, so silently
   * dropping either kind would show a plan the organizer does not have.
   *
   * A failed first load used to leave the skeleton up; a failed reload after a
   * delete used to be reported as a failure to delete.
   */
  const refresh = useCallback(async () => {
    try {
      const [nextExceptions, nextOverrides] = await Promise.all([
        callProtected((token) => api.availability.getExceptions(token)),
        callProtected((token) => api.availability.getOverrides(token)),
      ]);
      setExceptions(nextExceptions);
      setOverrides(nextOverrides);
      setLoaded(true);
      setLoadError(null);
    } catch (err) {
      setLoadError(errorMessage(err, 'Could not load your date exceptions.'));
    } finally {
      setLoading(false);
    }
  }, [callProtected]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const retryLoad = () => {
    setLoading(true);
    void refresh();
  };

  const resetHoursForm = () => {
    setHoursDate('');
    setClosed(false);
    setRanges([DEFAULT_RANGE]);
    setNote('');
    setEditingDate(null);
    setError(null);
    // Leaving edit mode - cancelled, saved, or the row deleted underneath it -
    // is the end of the guarded window, so the empty form is the new baseline.
    markSaved();
  };

  const switchKind = (next: ExceptionKind) => {
    setKind(next);
    setError(null);
    // Leaving the hours form abandons an edit in progress; staying on a
    // half-filled edit that is no longer visible is how a later save writes to
    // a date the organizer has stopped thinking about.
    if (next === 'block' && editingDate !== null) resetHoursForm();
  };

  const startEditingHours = (override: AvailabilityOverrideDto) => {
    setKind('hours');
    setHoursDate(override.date);
    setClosed(override.isClosed);
    setRanges(override.isClosed ? [DEFAULT_RANGE] : toRangeInputs(override.ranges));
    setNote(override.note ?? '');
    setEditingDate(override.date);
    setError(null);
    // The row as it stands on the server is what any later edit is measured
    // against.
    markSaved();
  };

  const submitBlock = async () => {
    // Caught here rather than at the API so the organizer gets the answer without
    // a round trip; the backend enforces the same rule regardless.
    if (endDate && endDate < date) {
      setError('The end date cannot be before the start date.');
      return;
    }
    await callProtected((token) =>
      api.availability.createException(
        token,
        date,
        wholeDay ? null : `${startTime}:00`,
        wholeDay ? null : `${endTime}:00`,
        type,
        reason || null,
        // Empty means a single day, which is what the endpoint did before ranges.
        endDate || null,
      ),
    );
    setDate('');
    setEndDate('');
    setReason('');
  };

  const submitHours = async () => {
    // Answered without a round trip; the backend enforces the same rules anyway.
    if (!closed && ranges.some((r) => r.start >= r.end)) {
      setError('Each time range must start before it ends.');
      return;
    }
    await callProtected((token) =>
      api.availability.saveOverride(
        token,
        hoursDate,
        // An empty array is what "closed" means on the wire - the same single
        // source of truth the entity uses, rather than a second flag.
        closed ? [] : ranges.map((r) => ({ start: `${r.start}:00`, end: `${r.end}:00` })),
        note || null,
      ),
    );
    resetHoursForm();
  };

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      if (kind === 'block') await submitBlock();
      else await submitHours();
      await refresh();
    } catch (err) {
      setError(
        errorMessage(
          err,
          kind === 'block'
            ? 'Failed to block these dates. Check the dates and times.'
            : 'Failed to save these hours. Check the date and times.',
        ),
      );
    } finally {
      setSubmitting(false);
    }
  };

  /*
   * A failed delete used to be an unhandled rejection: the row stayed exactly
   * where it was and nothing said why, which reads as a button that does
   * nothing. Deleting is the half of this screen that reopens availability, so
   * "it did not happen" is the one outcome an organizer has to be told about.
   */
  const deleteException = async (exception: AvailabilityExceptionDto) => {
    setListError(null);
    setDeleting(true);
    try {
      await callProtected((token) => api.availability.deleteException(token, exception.id));
      setConfirmingKey(null);
      await refresh();
    } catch (err) {
      setListError(errorMessage(err, 'Could not remove this blocked period. Please try again.'));
    } finally {
      setDeleting(false);
    }
  };

  const deleteOverride = async (override: AvailabilityOverrideDto) => {
    setListError(null);
    setDeleting(true);
    try {
      await callProtected((token) => api.availability.deleteOverride(token, override.id));
      setConfirmingKey(null);
      if (editingDate === override.date) resetHoursForm();
      await refresh();
    } catch (err) {
      setListError(errorMessage(err, 'Could not remove these hours. Please try again.'));
    } finally {
      setDeleting(false);
    }
  };

  const updateRange = (index: number, patch: Partial<RangeInput>) =>
    setRanges((prev) => prev.map((r, i) => (i === index ? { ...r, ...patch } : r)));

  const entries = mergeDateExceptions(exceptions, overrides);

  return (
    <div className={FORM_COLUMN}>
      <PageHeader
        title="Date exceptions"
        description="Dates that do not follow your weekly working hours - days you are away, and days you work different hours. These apply to every booking page you own."
      />

      {prompt}

      <Section title="Add an exception" divided={false}>
        {/* The form keeps its card: it is a distinct object sitting above a list
            of a different kind of thing, which is exactly what a border is for. */}
        <form onSubmit={handleSubmit} className={`space-y-4 ${CARD} p-5`}>
          {/* A real fieldset rather than a styled toggle: this is a genuine
              choice between two mutually exclusive things, which is what native
              radios already are - and what makes the rest of the form change
              meaning. */}
          <fieldset>
            <legend className={`${LABEL} mb-2`}>What is different about this date?</legend>
            <div className="flex flex-wrap gap-x-6 gap-y-2">
              <label className="flex cursor-pointer items-center gap-2 text-sm text-gray-700">
                <input
                  type="radio"
                  name="exception-kind"
                  className={CHECKBOX}
                  checked={kind === 'block'}
                  onChange={() => switchKind('block')}
                />
                I am unavailable
              </label>
              <label className="flex cursor-pointer items-center gap-2 text-sm text-gray-700">
                <input
                  type="radio"
                  name="exception-kind"
                  className={CHECKBOX}
                  checked={kind === 'hours'}
                  onChange={() => switchKind('hours')}
                />
                I work different hours
              </label>
            </div>
            <p className={`${META} mt-2`}>
              {kind === 'block'
                ? 'Blocks a single day or a whole period. Nothing can be booked in it.'
                : 'Replaces your weekly hours on one date - including opening a day you normally do not work.'}
            </p>
          </fieldset>

          {kind === 'block' ? (
            <BlockFields
              date={date} setDate={setDate}
              endDate={endDate} setEndDate={setEndDate}
              wholeDay={wholeDay} setWholeDay={setWholeDay}
              startTime={startTime} setStartTime={setStartTime}
              endTime={endTime} setEndTime={setEndTime}
              type={type} setType={setType}
              reason={reason} setReason={setReason}
            />
          ) : (
            <HoursFields
              date={hoursDate} setDate={setHoursDate}
              locked={editingDate !== null}
              closed={closed} setClosed={setClosed}
              ranges={ranges} setRanges={setRanges} updateRange={updateRange}
              note={note} setNote={setNote}
            />
          )}

          {error && <InlineNotice tone="error">{error}</InlineNotice>}

          <div className="flex flex-wrap gap-2">
            <button type="submit" disabled={submitting} className={BUTTON_PRIMARY}>
              {submitting
                ? 'Saving…'
                : kind === 'block'
                  ? 'Block these dates'
                  : editingDate
                    ? 'Save changes'
                    : 'Save these hours'}
            </button>
            {kind === 'hours' && editingDate && (
              <button type="button" onClick={resetHoursForm} className={BUTTON_SECONDARY}>
                Cancel
              </button>
            )}
          </div>
        </form>
      </Section>

      {/* "All", not "Upcoming": both endpoints are called without a date range
          and return the organizer's whole history, so anything narrower would
          be a claim the list does not keep. */}
      <Section
        title="All exceptions"
        className="mt-8"
        description="In date order. A blocked date always wins - it closes the day even if you have set hours for it."
      >
        {listError && <InlineNotice tone="error" className="mb-4">{listError}</InlineNotice>}

        {/* Distinct from `listError` above, which is a delete that was refused.
            This is the list itself failing to arrive - and until it has arrived
            once, it replaces the empty state rather than sitting above it,
            because "No exceptions yet" would tell an organizer their vacation
            is not blocked. */}
        {loadError && <LoadError message={loadError} onRetry={retryLoad} className="mb-4" />}

        {loading && <SkeletonLines lines={3} />}

        {!loading && loaded && entries.length === 0 && (
          <EmptyState
            icon={CalendarRange}
            title="No exceptions yet"
            description="Every date follows your weekly working hours. Add an exception above for a day off, or for a day you work different hours."
          />
        )}

        {entries.length > 0 && (
          <ul className={`${CARD} divide-y divide-gray-100`}>
            {entries.map((entry) => {
              const overridden =
                entry.kind === 'hours' && isBlockedByWholeDay(entry.override, exceptions);
              return (
                <li key={entry.key} className="px-5 py-3.5">
                  <div className="flex items-center justify-between gap-4">
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
                      <span className="text-[15px] font-medium text-gray-900">{entryPeriod(entry)}</span>
                      {/* The kind, said rather than implied by which list it is
                          in - the one thing consolidation would otherwise cost. */}
                      {entry.kind === 'block'
                        ? <StatusPill tone="danger">Unavailable</StatusPill>
                        : <StatusPill tone="info">Different hours</StatusPill>}
                    </div>
                    <p className={META}>
                      {entrySummary(entry)}
                      {entry.kind === 'block' && (
                        <>
                          <Dot />
                          {entry.exception.type}
                        </>
                      )}
                      {entry.kind === 'block' && entry.exception.reason && (
                        <>
                          <Dot />
                          <span className="text-gray-500">{entry.exception.reason}</span>
                        </>
                      )}
                      {entry.kind === 'hours' && entry.override.note && (
                        <>
                          <Dot />
                          <span className="text-gray-500">{entry.override.note}</span>
                        </>
                      )}
                    </p>
                    {overridden && (
                      <p className="mt-1 text-[13px] text-amber-700">
                        These hours have no effect - this date is blocked.
                      </p>
                    )}
                  </div>

                  <div className="flex shrink-0 gap-1">
                    {entry.kind === 'hours' && (
                      <button
                        type="button"
                        onClick={() => startEditingHours(entry.override)}
                        aria-label={`Edit hours for ${formatCalendarDate(entry.override.date)}`}
                        className={BUTTON_GHOST}
                      >
                        Edit
                      </button>
                    )}
                    {/* Hidden while its own panel is open - the panel's confirm
                        button carries the same words. */}
                    {confirmingKey !== entry.key && (
                      <button
                        type="button"
                        onClick={() => { setConfirmingKey(entry.key); setListError(null); }}
                        aria-label={
                          entry.kind === 'block'
                            ? `Delete blocked period ${entryPeriod(entry)}`
                            : `Delete hours for ${formatCalendarDate(entry.override.date)}`
                        }
                        className={BUTTON_DANGER}
                      >
                        <Trash2 className="h-4 w-4" aria-hidden="true" />
                        Delete
                      </button>
                    )}
                  </div>
                  </div>

                  {/* Both kinds reopen availability, in different ways - a block
                      lets the weekly hours apply again, an override hands the
                      date back to them - so each says which. */}
                  {confirmingKey === entry.key && (
                    <div className="mt-3">
                      {entry.kind === 'block' ? (
                        <ConfirmPanel
                          title={`Delete this blocked period?`}
                          confirmLabel="Delete blocked period"
                          busyLabel="Deleting…"
                          cancelLabel="Keep it"
                          busy={deleting}
                          onConfirm={() => void deleteException(entry.exception)}
                          onCancel={() => setConfirmingKey(null)}
                        >
                          <p>
                            {entryPeriod(entry)} follows your weekly working hours again, and guests can book
                            in it.
                          </p>
                        </ConfirmPanel>
                      ) : (
                        <ConfirmPanel
                          title={`Delete these hours?`}
                          confirmLabel="Delete hours"
                          busyLabel="Deleting…"
                          cancelLabel="Keep them"
                          busy={deleting}
                          onConfirm={() => void deleteOverride(entry.override)}
                          onCancel={() => setConfirmingKey(null)}
                        >
                          <p>
                            {formatCalendarDate(entry.override.date)} goes back to your weekly working hours.
                            To close the date instead, save it with no time ranges.
                          </p>
                        </ConfirmPanel>
                      )}
                    </div>
                  )}
                </li>
              );
            })}
          </ul>
        )}
      </Section>
    </div>
  );
}

function Dot() {
  return <span aria-hidden="true" className="px-1.5 text-gray-300">·</span>;
}

function BlockFields({
  date, setDate, endDate, setEndDate, wholeDay, setWholeDay,
  startTime, setStartTime, endTime, setEndTime, type, setType, reason, setReason,
}: {
  date: string; setDate: (v: string) => void;
  endDate: string; setEndDate: (v: string) => void;
  wholeDay: boolean; setWholeDay: (v: boolean) => void;
  startTime: string; setStartTime: (v: string) => void;
  endTime: string; setEndTime: (v: string) => void;
  type: AvailabilityExceptionType; setType: (v: AvailabilityExceptionType) => void;
  reason: string; setReason: (v: string) => void;
}) {
  return (
    <>
      <div className="flex flex-wrap items-end gap-3">
        <Field label="From">
          {(control) => (
            <input {...control} type="date" required className={INPUT} value={date} onChange={(e) => setDate(e.target.value)} />
          )}
        </Field>

        <Field label="To" optional>
          {(control) => (
            <input
              {...control}
              type="date"
              className={INPUT}
              value={endDate}
              // The browser's own picker will not offer a day before the start.
              min={date || undefined}
              onChange={(e) => setEndDate(e.target.value)}
            />
          )}
        </Field>

        <Field label="Type">
          {(control) => (
            <select {...control} className={INPUT} value={type} onChange={(e) => setType(e.target.value as AvailabilityExceptionType)}>
              {EXCEPTION_TYPES.map((t) => (
                <option key={t} value={t}>{t}</option>
              ))}
            </select>
          )}
        </Field>

        <label className="flex cursor-pointer items-center gap-2 pb-2 text-sm text-gray-700">
          <input type="checkbox" className={CHECKBOX} checked={wholeDay} onChange={(e) => setWholeDay(e.target.checked)} />
          Whole day
        </label>

        {!wholeDay && (
          <>
            <Field label="Between">
              {(control) => (
                <input {...control} type="time" className={INPUT} value={startTime} onChange={(e) => setStartTime(e.target.value)} />
              )}
            </Field>
            <Field label="and">
              {(control) => (
                <input {...control} type="time" className={INPUT} value={endTime} onChange={(e) => setEndTime(e.target.value)} />
              )}
            </Field>
          </>
        )}
      </div>

      {!wholeDay && endDate && endDate !== date && (
        <p className={META}>These hours will be blocked on <em>every</em> day in the range.</p>
      )}

      <Field label="Reason" optional>
        {(control) => (
          <input
            {...control}
            className={`${INPUT} w-full`}
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            placeholder="Summer vacation"
          />
        )}
      </Field>
    </>
  );
}

function HoursFields({
  date, setDate, locked, closed, setClosed, ranges, setRanges, updateRange, note, setNote,
}: {
  date: string; setDate: (v: string) => void;
  locked: boolean;
  closed: boolean; setClosed: (v: boolean) => void;
  ranges: RangeInput[];
  setRanges: (updater: (prev: RangeInput[]) => RangeInput[]) => void;
  updateRange: (index: number, patch: Partial<RangeInput>) => void;
  note: string; setNote: (v: string) => void;
}) {
  return (
    <>
      <div className="flex flex-wrap items-end gap-3">
        <Field label="Date">
          {(control) => (
            <input
              {...control}
              type="date"
              required
              // Locked while editing: the date identifies the override, so
              // changing it here would silently create a second one and leave
              // the original behind.
              readOnly={locked}
              className={`${INPUT} ${locked ? 'bg-gray-50 text-gray-500' : ''}`}
              value={date}
              onChange={(e) => setDate(e.target.value)}
            />
          )}
        </Field>

        <label className="flex cursor-pointer items-center gap-2 pb-2 text-sm text-gray-700">
          <input type="checkbox" className={CHECKBOX} checked={closed} onChange={(e) => setClosed(e.target.checked)} />
          Closed all day
        </label>
      </div>

      {!closed && (
        // A real group, like the radio group at the top of this form: the
        // caption names the range rows below it, each of which carries only its
        // own "Range N start"/"Range N end" label - so as a bare span it named
        // them to nobody. `min-w-0` because a fieldset's UA default is
        // `min-width: min-content`, which the div it replaces did not have
        //.
        <fieldset className="min-w-0 space-y-2">
          <legend className={LABEL}>Available hours</legend>
          {ranges.map((range, index) => (
            // Index key: these rows have no id until saved, and the list is
            // only ever appended to or spliced by the same index.
            <div key={index} className="flex flex-wrap items-center gap-2">
              <input
                type="time"
                aria-label={`Range ${index + 1} start`}
                className={INPUT}
                value={range.start}
                onChange={(e) => updateRange(index, { start: e.target.value })}
              />
              <span className="text-gray-500">–</span>
              <input
                type="time"
                aria-label={`Range ${index + 1} end`}
                className={INPUT}
                value={range.end}
                onChange={(e) => updateRange(index, { end: e.target.value })}
              />
              {ranges.length > 1 && (
                <button
                  type="button"
                  aria-label={`Remove range ${index + 1}`}
                  onClick={() => setRanges((prev) => prev.filter((_, i) => i !== index))}
                  className={BUTTON_GHOST}
                >
                  <X className="h-4 w-4" aria-hidden="true" />
                </button>
              )}
            </div>
          ))}

          {ranges.length < MAX_OVERRIDE_RANGES && (
            <button
              type="button"
              onClick={() => setRanges((prev) => [...prev, DEFAULT_RANGE])}
              className={BUTTON_SECONDARY}
            >
              <Plus className="h-4 w-4" aria-hidden="true" />
              Add another range
            </button>
          )}
          <p className={META}>A gap between two ranges is a break - the same way your weekly hours work.</p>
        </fieldset>
      )}

      <Field label="Note" optional>
        {(control) => (
          <input
            {...control}
            className={`${INPUT} w-full`}
            value={note}
            onChange={(e) => setNote(e.target.value)}
            placeholder="Conference in the morning"
          />
        )}
      </Field>

      {/* Deleting and saving-empty are genuinely different operations - the
          first restores the weekly schedule, the second closes the date - and
          nothing else on the screen could tell them apart. */}
      <p className={META}>
        Saved hours replace your weekly schedule for that date. Delete them to go back to it, or
        save with <strong className="font-medium text-gray-700">Closed all day</strong> to close
        the date entirely.
      </p>
    </>
  );
}
