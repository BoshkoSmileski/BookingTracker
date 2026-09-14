import { useCallback, useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { Info, Trash2 } from 'lucide-react';
import { useParams } from 'react-router-dom';
import {
  BUTTON_DANGER, BUTTON_PRIMARY, CARD, ConfirmPanel, EmptyState, Field, FORM_COLUMN,
  InlineNotice, INPUT, LoadError, META, PageHeader, SkeletonLines
} from '../components/ui';
import { useAuth } from '../contexts/AuthContext';
import { api, errorMessage } from '../lib/api';
import type { BookingInstructionDto } from '../lib/types';
import { useDocumentTitle } from '../lib/pageTitle';

const inputClass = INPUT;

/**
 * Lets an organizer write the guidance visitors see before booking.
 *
 * This screen used to be called "Booking questions", which described neither
 * what an organizer writes here nor what a visitor does with it - the page then
 * had to spend a sentence explaining that the "questions" are not answered.
 * Naming it for what it is removes the need for that explanation entirely.
 */
export function BookingInstructionsPage() {
  useDocumentTitle('Booking instructions');
  const { pageId = '' } = useParams<{ pageId: string }>();
  const { callProtected } = useAuth();
  const [instructions, setInstructions] = useState<BookingInstructionDto[]>([]);
  const [loading, setLoading] = useState(true);
  /** Whether the list has ever been read. An empty list is a normal outcome. */
  const [loaded, setLoaded] = useState(false);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [text, setText] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  /** The instruction whose removal is being confirmed, if any. */
  const [confirmingId, setConfirmingId] = useState<string | null>(null);
  const [removing, setRemoving] = useState(false);

  // See BookingFormPage.refresh - same shape, same two reasons: a failed first
  // load used to leave the skeleton up, and a failed reload after adding an
  // instruction used to be reported as a failure to add it.
  const refresh = useCallback(async () => {
    try {
      const page = await callProtected((token) => api.organizer.getBookingPage(token, pageId));
      setInstructions(page.instructions);
      setLoaded(true);
      setLoadError(null);
    } catch (e) {
      setLoadError(errorMessage(e, 'Could not load your booking instructions.'));
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

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      await callProtected((token) => api.organizer.addBookingInstruction(token, pageId, text));
      setText('');
      await refresh();
    } catch (e) {
      setError(errorMessage(e, 'Could not add this instruction. Please try again.'));
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (id: string) => {
    setError(null);
    setRemoving(true);
    try {
      await callProtected((token) => api.organizer.removeBookingInstruction(token, pageId, id));
      setConfirmingId(null);
      await refresh();
    } catch (e) {
      setError(errorMessage(e, 'Could not remove this instruction. Please try again.'));
    } finally {
      setRemoving(false);
    }
  };

  return (
    <div className={FORM_COLUMN}>
      <PageHeader
        title="Booking instructions"
        description="Display important information for visitors before they complete a booking — what to bring, how to prepare, or anything they should know in advance."
      />

      <form onSubmit={handleSubmit} className={`mb-6 ${CARD} p-5`}>
        {/* The submit button shares the row with the input, so the control is
            the <input> and only the <input>. The hint spelled `META` out by
            hand and sat on `mt-2` rather than `HELP_GAP`. */}
        <Field
          label="Instruction"
          hint="Visitors read this on your booking page. There is nothing for them to fill in, so write it as a statement."
        >
          {(control) => (
            <div className="flex flex-wrap items-start gap-2">
              <input
                {...control}
                className={`${inputClass} min-w-0 flex-1`}
                value={text}
                onChange={(e) => setText(e.target.value)}
                placeholder="Please have your account number ready"
                maxLength={300}
                required
              />
              <button type="submit" disabled={submitting} className={BUTTON_PRIMARY}>
                {submitting ? 'Adding…' : 'Add instruction'}
              </button>
            </div>
          )}
        </Field>
      </form>

      {error && <InlineNotice tone="error" className="mb-3">{error}</InlineNotice>}

      {loadError && <LoadError message={loadError} onRetry={retryLoad} className="mb-3" />}

      {loading && <SkeletonLines lines={3} />}

      {!loading && loaded && instructions.length === 0 && (
        <EmptyState
          icon={Info}
          title="No instructions yet"
          description="Anything you add here appears on your booking page before a visitor confirms."
        />
      )}

      {!loading && instructions.length > 0 && (
        <>
          <p className={`mb-2 ${META}`}>Shown on your booking page, in this order.</p>
          <ul className={`${CARD} divide-y divide-gray-100`}>
            {instructions.map((instruction, index) => (
              <li key={instruction.id} className="px-5 py-3.5">
                <div className="flex items-start justify-between gap-4">
                  <span className="flex min-w-0 items-baseline gap-2.5">
                    {/* A number rather than a repeated icon: the order is the point
                        here, and an identical glyph on every row carries none of it. */}
                    <span aria-hidden="true" className="w-5 shrink-0 text-[13px] tabular-nums text-gray-500">
                      {index + 1}
                    </span>
                    <span className="text-[15px] text-gray-900">{instruction.text}</span>
                  </span>
                  {/* Hidden while its own panel is open: the panel's confirm
                      button carries the same words, and two controls with one
                      accessible name is ambiguous to anyone not looking at the
                      screen. */}
                  {confirmingId !== instruction.id && (
                    <button
                      type="button"
                      onClick={() => { setConfirmingId(instruction.id); setError(null); }}
                      aria-label={`Remove instruction: ${instruction.text}`}
                      className={`${BUTTON_DANGER} shrink-0`}
                    >
                      <Trash2 className="h-4 w-4" aria-hidden="true" />
                      Remove
                    </button>
                  )}
                </div>

                {/* In the row, so the instruction being removed stays on screen
                    and named while the question is asked. */}
                {confirmingId === instruction.id && (
                  <div className="mt-3">
                    <ConfirmPanel
                      title="Remove this instruction?"
                      confirmLabel="Remove instruction"
                      busyLabel="Removing…"
                      cancelLabel="Keep it"
                      busy={removing}
                      onConfirm={() => void handleDelete(instruction.id)}
                      onCancel={() => setConfirmingId(null)}
                    >
                      <p>It stops appearing on your booking page, so guests booking from now on will not see it.</p>
                    </ConfirmPanel>
                  </div>
                )}
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  );
}
