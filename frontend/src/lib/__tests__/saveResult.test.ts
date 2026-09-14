import { describe, expect, it } from 'vitest';
import { ApiError, NetworkError } from '../api';
import { SAVED, failed, saveFailed, saved, unmappedSaveError } from '../saveResult';

/**
 * The one shape every screen reports an action in.
 *
 * It gained `saved(message)` and `failed(message)` because the calendar and
 * notification screens each kept a private `{ type, message }` state - not
 * because the behaviour differed, but because "Saved." was too narrow for
 * "Google Calendar disconnected." A second vocabulary for one idea is exactly
 * what this module exists to prevent, so the fix was a parameter.
 */
describe('saveResult', () => {
  it('keeps SAVED as the default success', () => {
    expect(SAVED).toEqual({ tone: 'success', message: 'Saved.' });
    expect(saved()).toEqual(SAVED);
  });

  it('carries an action-specific success in the same shape', () => {
    expect(saved('Google Calendar disconnected.')).toEqual({
      tone: 'success',
      message: 'Google Calendar disconnected.',
    });
  });

  it('reports a failure the app already has the words for', () => {
    // `syncNow` resolves even when the sync failed - the outcome arrives on the
    // DTO, so there is no error to resolve, only one to report.
    expect(failed('Google API returned 503')).toEqual({
      tone: 'error',
      message: 'Google API returned 503',
    });
  });

  it('reports an unreachable server as connectivity, not as the caller’s fallback', () => {
    const result = saveFailed(new NetworkError(), 'Failed to disconnect. Please try again.');

    expect(result?.tone).toBe('error');
    expect(result?.message).toMatch(/could not reach the server/i);
    expect(result?.message).not.toMatch(/failed to disconnect/i);
  });

  it('prefers what the server said over the fallback', () => {
    const result = saveFailed(new ApiError(403, { title: 'Your Google authorization has expired.' }), 'Failed to save.');

    expect(result?.message).toBe('Your Google authorization has expired.');
  });

  it('falls back only for an error that is neither', () => {
    expect(saveFailed(new Error('boom'), 'Failed to save.')?.message).toBe('Failed to save.');
  });

  it('leaves nothing for the summary when every message reached a field', () => {
    const error = new ApiError(400, {
      title: 'Validation failed',
      errors: { TimeZoneId: ["'Nowhere/Land' is not a recognized IANA time zone id."] },
    });

    expect(unmappedSaveError(error, ['TimeZoneId'])).toBeNull();
  });

  it('keeps a message no field claimed', () => {
    const error = new ApiError(400, {
      title: 'Validation failed',
      errors: { 'Days[0].Intervals': ['Intervals within a single day must not overlap.'] },
    });

    expect(unmappedSaveError(error, ['TimeZoneId'])).toEqual({
      tone: 'error',
      message: 'Intervals within a single day must not overlap.',
    });
  });
});
