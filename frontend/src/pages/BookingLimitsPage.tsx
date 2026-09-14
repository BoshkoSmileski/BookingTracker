import { useCallback, useEffect, useMemo, useState } from 'react';
import type { FormEvent } from 'react';
import { useParams } from 'react-router-dom';
import {
  BUTTON_PRIMARY, Field, FORM_COLUMN, InlineNotice, INPUT, LoadError, PageHeader, SkeletonLines
} from '../components/ui';
import { useAuth } from '../contexts/AuthContext';
import { useUnsavedChanges } from '../hooks/useUnsavedChanges';
import { api, errorMessage } from '../lib/api';
import { SAVED, saveFailed } from '../lib/saveResult';
import type { SaveResult } from '../lib/saveResult';
import { useDocumentTitle } from '../lib/pageTitle';

const inputClass = `${INPUT} w-32`;

function toOptionalInt(value: string): number | null {
  if (value.trim() === '') return null;
  const parsed = Number(value);
  return Number.isNaN(parsed) ? null : parsed;
}

export function BookingLimitsPage() {
  useDocumentTitle('Booking limits');
  const { pageId = '' } = useParams<{ pageId: string }>();
  const { callProtected } = useAuth();
  const [minNotice, setMinNotice] = useState('');
  const [maxWindow, setMaxWindow] = useState('');
  const [maxPerDay, setMaxPerDay] = useState('');
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [result, setResult] = useState<SaveResult>(null);

  const draft = useMemo(() => ({ minNotice, maxWindow, maxPerDay }), [minNotice, maxWindow, maxPerDay]);
  const { markSaved, prompt } = useUnsavedChanges(draft);

  const load = useCallback(() => {
    setLoading(true);
    setLoadError(null);
    callProtected((token) => api.organizer.getBookingPage(token, pageId))
      .then((page) => {
        setMinNotice(page.minNoticeMinutes === null ? '' : String(page.minNoticeMinutes));
        setMaxWindow(page.maxBookingWindowDays === null ? '' : String(page.maxBookingWindowDays));
        setMaxPerDay(page.maxBookingsPerDay === null ? '' : String(page.maxBookingsPerDay));
        markSaved();
      })
      // Showing an empty form here would be worse than showing nothing: every
      // field means "no limit" when blank, so a failed load would read as three
      // limits the organizer had never set.
      .catch((e) => setLoadError(errorMessage(e, 'Could not load these booking limits.')))
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
      await callProtected((token) =>
        api.organizer.updateBookingPageLimits(
          token, pageId, toOptionalInt(minNotice), toOptionalInt(maxWindow), toOptionalInt(maxPerDay),
        ),
      );
      setResult(SAVED);
      markSaved();
    } catch (err) {
      setResult(saveFailed(err));
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className={FORM_COLUMN}>
      <PageHeader
        title="Booking limits"
        description="Guardrails on when guests can book. Leave any of these blank for no limit."
      />

      {prompt}

      {loading ? (
        <SkeletonLines lines={4} />
      ) : loadError ? (
        <LoadError message={loadError} onRetry={load} />
      ) : (
        <form onSubmit={handleSubmit} className="space-y-4">
          {/* No per-field "(optional)" marker even though all three are: the
              page description already says "Leave any of these blank for no
              limit" once, and three markers repeating it is the duplication
              the one-spelling rule is about, not an instance of it. */}
          <Field label="Minimum notice (minutes)">
            {(control) => (
              <input
                {...control}
                type="number"
                min={0}
                className={inputClass}
                value={minNotice}
                onChange={(e) => setMinNotice(e.target.value)}
              />
            )}
          </Field>
          <Field label="Max booking window (days)">
            {(control) => (
              <input
                {...control}
                type="number"
                min={1}
                className={inputClass}
                value={maxWindow}
                onChange={(e) => setMaxWindow(e.target.value)}
              />
            )}
          </Field>
          <Field label="Max bookings per day">
            {(control) => (
              <input
                {...control}
                type="number"
                min={1}
                className={inputClass}
                value={maxPerDay}
                onChange={(e) => setMaxPerDay(e.target.value)}
              />
            )}
          </Field>

          {result && <InlineNotice tone={result.tone}>{result.message}</InlineNotice>}

          <button type="submit" disabled={saving} className={BUTTON_PRIMARY}>
            {saving ? 'Saving…' : 'Save'}
          </button>
        </form>
      )}
    </div>
  );
}
