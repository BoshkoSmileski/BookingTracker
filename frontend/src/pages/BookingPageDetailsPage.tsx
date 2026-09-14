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

const inputClass = `${INPUT} w-full`;

export function BookingPageDetailsPage() {
  useDocumentTitle('Booking page details');
  const { pageId = '' } = useParams<{ pageId: string }>();
  const { callProtected } = useAuth();
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [result, setResult] = useState<SaveResult>(null);

  const draft = useMemo(() => ({ title, description }), [title, description]);
  const { markSaved, prompt } = useUnsavedChanges(draft);

  // Had no `catch`, so a failed load left the skeleton up for ever - the form
  // never appeared and nothing said why.
  const load = useCallback(() => {
    setLoading(true);
    setLoadError(null);
    callProtected((token) => api.organizer.getBookingPage(token, pageId))
      .then((page) => {
        setTitle(page.title);
        setDescription(page.description ?? '');
        markSaved();
      })
      .catch((e) => setLoadError(errorMessage(e, 'Could not load this booking page.')))
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
      await callProtected((token) => api.organizer.updateBookingPageDetails(token, pageId, title, description || null));
      setResult(SAVED);
      // Only after the request resolved: a rejected save must leave the form
      // dirty so the warning still fires.
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
        title="Details"
        description="The name and blurb guests see at the top of this booking page."
      />

      {prompt}

      {loading ? (
        <SkeletonLines lines={4} />
      ) : loadError ? (
        <LoadError message={loadError} onRetry={load} />
      ) : (
        // No card: a box drawn around the only thing on the page separates it
        // from nothing. The page title already scopes these two fields.
        <form onSubmit={handleSubmit} className="space-y-4">
          <Field label="Title">
            {(control) => (
              <input {...control} className={inputClass} value={title} onChange={(e) => setTitle(e.target.value)} required />
            )}
          </Field>
          {/* Marked optional because it is - the handler sends `description ||
              null`, and nothing on this screen said so. Title carries `required`
              and needs no marker, which is the required-by-default rule
              `FieldLabel` was written for. */}
          <Field label="Description" optional>
            {(control) => (
              <textarea
                {...control}
                className={inputClass}
                rows={3}
                value={description}
                onChange={(e) => setDescription(e.target.value)}
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
