import { useCallback, useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { ListChecks, Trash2 } from 'lucide-react';
import { useParams } from 'react-router-dom';
import {
  BUTTON_DANGER, BUTTON_PRIMARY, CARD, CHECKBOX, ConfirmPanel, EmptyState, Field,
  FORM_COLUMN, InlineNotice, INPUT, LoadError, META, PageHeader, SkeletonLines, StatusPill
} from '../components/ui';
import { useAuth } from '../contexts/AuthContext';
import { api, errorMessage } from '../lib/api';
import { CUSTOM_FIELD_LABEL_MAX_LENGTH, MAX_FORM_FIELDS } from '../lib/config';
import type { BookingFieldType, BookingFormFieldDto } from '../lib/types';
import { useDocumentTitle } from '../lib/pageTitle';

/**
 * Lets an organizer decide what a visitor is asked while booking.
 *
 * The counterpart to BookingInstructionsPage: that screen holds what visitors
 * *read*, this one holds what they *answer*. Neither is called a "question" in
 * code - see lib/types.ts - but this is the one an organizer would call that,
 * so the description says so where the word is unambiguous.
 */
export function BookingFormPage() {
  useDocumentTitle('Booking form');
  const { pageId = '' } = useParams<{ pageId: string }>();
  const { callProtected } = useAuth();
  const [fields, setFields] = useState<BookingFormFieldDto[]>([]);
  const [loading, setLoading] = useState(true);
  // Not `fields.length`: an organizer with no questions is the normal case, so
  // an empty list cannot say whether the list was ever read.
  const [loaded, setLoaded] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [label, setLabel] = useState('');
  const [type, setType] = useState<BookingFieldType>('ShortText');
  const [isRequired, setIsRequired] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  /** The question whose removal is being confirmed, if any. */
  const [confirmingId, setConfirmingId] = useState<string | null>(null);
  const [removing, setRemoving] = useState(false);

  /**
   * Catches its own failure rather than rejecting.
   *
   * Two things turned on that. A failed *initial* load left the skeleton up for
   * ever, and had it resolved instead it would have shown "No questions yet" -
   * an empty state, for a list nobody managed to read. And because this also
   * runs after adding or removing a question, an unhandled rejection here
   * surfaced through the caller's `catch` as "Could not add this question",
   * which is precisely wrong when the add succeeded and only the reload did not.
   */
  const refresh = useCallback(async () => {
    try {
      const page = await callProtected((token) => api.organizer.getBookingPage(token, pageId));
      setFields(page.formFields);
      setLoaded(true);
      setLoadError(null);
    } catch (e) {
      setLoadError(errorMessage(e, 'Could not load your booking form.'));
    } finally {
      setLoading(false);
    }
  }, [callProtected, pageId]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const retryLoad = () => {
    setLoading(true);
    void refresh();
  };

  const atLimit = fields.length >= MAX_FORM_FIELDS;

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      await callProtected((token) => api.organizer.addBookingFormField(token, pageId, label, type, isRequired));
      // Only the label resets: an organizer adding "Company" then "Job title"
      // almost always wants the same type and requiredness again.
      setLabel('');
      await refresh();
    } catch (e) {
      setError(errorMessage(e, 'Could not add this question. Please try again.'));
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (field: BookingFormFieldDto) => {
    setError(null);
    setRemoving(true);
    try {
      await callProtected((token) => api.organizer.removeBookingFormField(token, pageId, field.id));
      setConfirmingId(null);
      await refresh();
    } catch (e) {
      setError(errorMessage(e, 'Could not remove this question. Please try again.'));
    } finally {
      setRemoving(false);
    }
  };

  return (
    <div className={FORM_COLUMN}>
      <PageHeader
        title="Booking form"
        description="Questions guests answer while booking — a company name, what they'd like to discuss, an account number. Answers arrive with the booking and appear on the session and in your confirmation email."
      />

      <form onSubmit={handleSubmit} className={`mb-6 ${CARD} p-5`}>
        <Field label="Question">
          {(control) => (
            <input
              {...control}
              className={`${INPUT} w-full`}
              value={label}
              onChange={(e) => setLabel(e.target.value)}
              placeholder="What would you like to discuss?"
              maxLength={CUSTOM_FIELD_LABEL_MAX_LENGTH}
              required
              disabled={atLimit}
            />
          )}
        </Field>

        <div className="mt-3 flex flex-wrap items-end gap-4">
          <Field label="Answer type">
            {(control) => (
              <select
                {...control}
                className={INPUT}
                value={type}
                onChange={(e) => setType(e.target.value as BookingFieldType)}
                disabled={atLimit}
              >
                <option value="ShortText">Short text</option>
                <option value="LongText">Long text</option>
              </select>
            )}
          </Field>

          <label className="flex items-center gap-2 py-2 text-[15px] text-gray-700">
            <input
              type="checkbox"
              className={CHECKBOX}
              checked={isRequired}
              onChange={(e) => setIsRequired(e.target.checked)}
              disabled={atLimit}
            />
            Required
          </label>

          <button type="submit" disabled={submitting || atLimit} className={`${BUTTON_PRIMARY} ml-auto`}>
            {submitting ? 'Adding…' : 'Add question'}
          </button>
        </div>

        <p className="mt-2 text-[13px] text-gray-500">
          {atLimit
            ? `You have reached the limit of ${MAX_FORM_FIELDS} questions. Remove one to add another.`
            : 'A required question must be answered before a guest can confirm their booking.'}
        </p>
      </form>

      {error && <InlineNotice tone="error" className="mb-3">{error}</InlineNotice>}

      {/* Never loaded: the error replaces the list, because an empty state here
          would say the organizer has no questions rather than that we could not
          find out. Once it has loaded, a failed reload keeps the list on screen
          and reports itself above it. */}
      {loadError && <LoadError message={loadError} onRetry={retryLoad} className="mb-3" />}

      {loading && <SkeletonLines lines={3} />}

      {!loading && loaded && fields.length === 0 && (
        <EmptyState
          icon={ListChecks}
          title="No questions yet"
          description="Your booking form asks for a name, email, phone and message. Anything you add here is asked as well."
        />
      )}

      {!loading && fields.length > 0 && (
        <>
          <p className={`mb-2 ${META}`}>Asked on your booking page, in this order.</p>
          <ul className={`${CARD} divide-y divide-gray-100`}>
            {fields.map((field, index) => (
              <li key={field.id} className="px-5 py-3.5">
                <div className="flex items-start justify-between gap-4">
                <span className="flex min-w-0 items-baseline gap-2.5">
                  <span aria-hidden="true" className="w-5 shrink-0 text-[13px] tabular-nums text-gray-500">
                    {index + 1}
                  </span>
                  <span className="min-w-0">
                    <span className="block text-[15px] break-words text-gray-900">{field.label}</span>
                    <span className={`mt-0.5 flex items-center gap-2 ${META}`}>
                      {field.type === 'LongText' ? 'Long text' : 'Short text'}
                      {/* Only "Required" gets a marker: optional is the default,
                          and pinning both makes neither stand out. */}
                      {field.isRequired && <StatusPill tone="warning">Required</StatusPill>}
                    </span>
                  </span>
                </span>
                {/* Hidden while its own panel is open - the panel's confirm
                    button carries the same words. */}
                {confirmingId !== field.id && (
                  <button
                    type="button"
                    onClick={() => { setConfirmingId(field.id); setError(null); }}
                    aria-label={`Remove question: ${field.label}`}
                    className={`${BUTTON_DANGER} shrink-0`}
                  >
                    <Trash2 className="h-4 w-4" aria-hidden="true" />
                    Remove
                  </button>
                )}
                </div>

                {confirmingId === field.id && (
                  <div className="mt-3">
                    <ConfirmPanel
                      title={`Remove "${field.label}"?`}
                      confirmLabel="Remove question"
                      busyLabel="Removing…"
                      cancelLabel="Keep it"
                      busy={removing}
                      onConfirm={() => void handleDelete(field)}
                      onCancel={() => setConfirmingId(null)}
                    >
                      <p>
                        It stops being asked on your booking page. Answers already given to it stay on the
                        bookings that have them.
                      </p>
                    </ConfirmPanel>
                  </div>
                )}
              </li>
            ))}
          </ul>
          <p className={`mt-2 ${META}`}>
            Removing a question stops it being asked. Answers already given to it stay on the bookings that
            have them.
          </p>
        </>
      )}
    </div>
  );
}
