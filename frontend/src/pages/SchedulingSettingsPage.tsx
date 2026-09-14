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

export function SchedulingSettingsPage() {
  useDocumentTitle('Duration & buffers');
  const { pageId = '' } = useParams<{ pageId: string }>();
  const { callProtected } = useAuth();
  const [duration, setDuration] = useState(30);
  const [bufferBefore, setBufferBefore] = useState(0);
  const [bufferAfter, setBufferAfter] = useState(0);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [result, setResult] = useState<SaveResult>(null);

  const draft = useMemo(
    () => ({ duration, bufferBefore, bufferAfter }),
    [duration, bufferBefore, bufferAfter],
  );
  const { markSaved, prompt } = useUnsavedChanges(draft);

  // Without a `catch` a failed load left the skeleton up permanently. Note the
  // form's defaults are 30/0/0, so falling through to it would have been worse
  // still: saving would silently overwrite the real duration with a guess.
  const load = useCallback(() => {
    setLoading(true);
    setLoadError(null);
    callProtected((token) => api.organizer.getMyBookingPages(token))
      .then((pages) => {
        const page = pages.find((p) => p.id === pageId);
        if (page) {
          setDuration(page.durationMinutes);
          setBufferBefore(page.bufferBeforeMinutes);
          setBufferAfter(page.bufferAfterMinutes);
        }
        markSaved();
      })
      .catch((e) => setLoadError(errorMessage(e, 'Could not load these settings.')))
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
      await callProtected((token) => api.availability.updateSchedulingSettings(token, pageId, duration, bufferBefore, bufferAfter));
      setResult(SAVED);
      markSaved();
    } catch (err) {
      setResult(saveFailed(err));
    } finally {
      setSaving(false);
    }
  };

  // The header stays in both states, so the screen keeps its identity while it
  // loads and while it explains that it could not.
  if (loading || loadError) {
    return (
      <div className={FORM_COLUMN}>
        <PageHeader title="Duration &amp; buffers" />
        {loading ? <SkeletonLines lines={4} /> : <LoadError message={loadError!} onRetry={load} />}
      </div>
    );
  }

  return (
    <div className={FORM_COLUMN}>
      <PageHeader
        title="Duration &amp; buffers"
        description="How long an appointment runs, and how much breathing room to keep either side of it."
      />

      {prompt}

      <form onSubmit={handleSubmit} className="space-y-4">
        <Field label="Appointment duration (minutes)">
          {(control) => (
            <input
              {...control}
              type="number"
              min={1}
              className={inputClass}
              value={duration}
              onChange={(e) => setDuration(Number(e.target.value))}
            />
          )}
        </Field>
        <Field label="Buffer before (minutes)">
          {(control) => (
            <input
              {...control}
              type="number"
              min={0}
              className={inputClass}
              value={bufferBefore}
              onChange={(e) => setBufferBefore(Number(e.target.value))}
            />
          )}
        </Field>
        <Field label="Buffer after (minutes)">
          {(control) => (
            <input
              {...control}
              type="number"
              min={0}
              className={inputClass}
              value={bufferAfter}
              onChange={(e) => setBufferAfter(Number(e.target.value))}
            />
          )}
        </Field>

        <p className="rounded-md bg-gray-50 px-3 py-2 text-xs text-gray-500">
          A {duration}-minute appointment with a {bufferAfter}-minute buffer after means the next appointment can start{' '}
          {duration + bufferAfter} minutes after this one begins.
        </p>

        {result && <InlineNotice tone={result.tone}>{result.message}</InlineNotice>}

        <button type="submit" disabled={saving} className={BUTTON_PRIMARY}>
          {saving ? 'Saving…' : 'Save'}
        </button>
      </form>
    </div>
  );
}
