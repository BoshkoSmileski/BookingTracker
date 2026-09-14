import { useCallback, useEffect, useRef, useState } from 'react';
import { api, errorMessage } from '../lib/api';
import { endOfMonthKey, startOfMonthKey } from '../lib/calendarGrid';
import type { AvailableSlotDto } from '../lib/types';

/**
 * Fetches a month's worth of available slots for a booking page, caching by
 * "slug:year-month" so navigating back to a month already viewed in this
 * session doesn't re-hit the API - the whole point of the "avoid unnecessary
 * API calls" requirement. The cache lives in a ref, so it's per mounted
 * wizard instance, not global (a stale cross-session cache would be worse
 * than no cache: an organizer changing their hours mid-session should be
 * reflected the next time the visitor opens the booking page).
 */
export function useAvailableSlots(slug: string, monthCursor: Date) {
  const cacheRef = useRef(new Map<string, AvailableSlotDto[]>());
  const [slots, setSlots] = useState<AvailableSlotDto[]>([]);
  const [loading, setLoading] = useState(true);
  /**
   * A failed fetch had no branch of its own: `.finally` cleared `loading` and
   * `slots` stayed empty, so the picker rendered "Nothing available in August"
   * with a "Try the next month" button - the app telling a guest the organizer
   * has no free time, when what actually happened is that nobody could ask.
   * Reported rather than inferred from an empty list.
   */
  const [error, setError] = useState<string | null>(null);
  /** Bumped by `reload` to re-run the effect for the month already showing. */
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    const from = startOfMonthKey(monthCursor);
    const to = endOfMonthKey(monthCursor);
    const cacheKey = `${slug}:${from}`;

    const cached = cacheRef.current.get(cacheKey);
    if (cached) {
      setSlots(cached);
      setError(null);
      setLoading(false);
      return;
    }

    let cancelled = false;
    setLoading(true);
    api
      .getAvailableSlots(slug, from, to)
      .then((result) => {
        if (cancelled) return;
        cacheRef.current.set(cacheKey, result);
        setSlots(result);
        setError(null);
      })
      .catch((e) => {
        if (cancelled) return;
        // Cleared, not left stale: the previous month's times must never be
        // offered as if they belonged to the month on screen.
        setSlots([]);
        setError(errorMessage(e, 'We could not load the available times.'));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [slug, monthCursor, attempt]);

  // Nothing was cached for a month that failed, so re-running the effect is the
  // whole of a retry.
  const reload = useCallback(() => setAttempt((n) => n + 1), []);

  return { slots, loading, error, reload };
}
