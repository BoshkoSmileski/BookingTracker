import { errorMessage, validationErrors } from './api';
import type { NoticeTone } from '../components/ui';

/**
 * The outcome of a settings save, in the shape `InlineNotice` renders.
 *
 * The four small settings forms each kept a `message: string | null` and printed
 * it in the same grey span whether the save had succeeded or failed - so the two
 * outcomes were indistinguishable, and a failure was never announced to
 * assistive tech. Carrying the tone alongside the text is what fixes that, and
 * having one helper means the wording does not drift between forms.
 */
export type SaveResult = { tone: NoticeTone; message: string } | null;

/**
 * A success, in whatever words the action calls for.
 *
 * Two screens (calendar, notifications) previously kept their own
 * `{ type: 'success' | 'error'; message }` state because "Saved." was too
 * narrow for what they had to report - "Google Calendar disconnected.",
 * "Now syncing with "Work"." That produced a second shape for the same idea,
 * which is exactly what this file exists to prevent, when the only thing
 * actually missing was a message parameter.
 */
export function saved(message = 'Saved.'): SaveResult {
  return { tone: 'success', message };
}

export const SAVED: SaveResult = saved();

/**
 * A rejection the app already has the words for - a status the server returned
 * in a DTO rather than threw, or an OAuth error code carried back on the URL.
 * Use `saveFailed` instead whenever there is a caught error to resolve.
 */
export function failed(message: string): SaveResult {
  return { tone: 'error', message };
}

/**
 * Routed through `errorMessage` rather than a hardcoded string, so an
 * unreachable API reports a connectivity problem instead of claiming the save
 * was rejected (see Coding Conventions).
 */
export function saveFailed(error: unknown, fallback = 'Failed to save.'): SaveResult {
  return failed(errorMessage(error, fallback));
}

/**
 * The part of a rejected save that no input on screen is already showing.
 *
 * For a form that places server messages beside their fields, `saveFailed`
 * would repeat every one of them in the summary notice as well. This returns
 * only what `mappedFields` did not claim - and `null` when a field claimed
 * everything, so the form shows each message exactly once, next to the thing
 * it is about. Anything that is not a validation rejection (a 500, an
 * unreachable API) still comes back as an ordinary failure.
 */
export function unmappedSaveError(
  error: unknown,
  mappedFields: string[],
  fallback = 'Failed to save.',
): SaveResult {
  const errors = validationErrors(error);
  if (!errors.hasAny) return saveFailed(error, fallback);

  const rest = errors.unmapped(mappedFields);
  return rest.length === 0 ? null : { tone: 'error', message: rest.join(' ') };
}
