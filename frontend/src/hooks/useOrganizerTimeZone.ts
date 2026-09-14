import { useEffect, useState } from 'react';
import { useAuth } from '../contexts/AuthContext';
import { api } from '../lib/api';

/**
 * The organizer's own IANA zone - the clock every `selectedDate`/`selectedTime`
 * in the API is on.
 *
 * There is exactly one per organizer (it lives on `WorkingSchedule`, which is
 * one row per organizer), so this is a fact about the account rather than about
 * any page. Screens that compare a booking's wall clock against "now" need it,
 * and the browser's zone is not a substitute: see `organizerToday` in
 * `lib/calendarDates.ts` for what that substitution cost.
 *
 * Returns `null` while loading **and** if the request fails - the callers treat
 * that as "fall back to the browser", which is exactly the behaviour they had
 * before this existed. A failure here must never take a screen down; the screen
 * has its own data and its own error reporting, and the worst case is the same
 * one-day edge that was previously the only case.
 */
export function useOrganizerTimeZone(): string | null {
  const { callProtected } = useAuth();
  const [timeZoneId, setTimeZoneId] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    callProtected((token) => api.availability.getSchedule(token))
      .then((schedule) => {
        if (!cancelled) setTimeZoneId(schedule?.timeZoneId ?? null);
      })
      .catch(() => {
        // Deliberately quiet - see the note above.
      });
    return () => {
      cancelled = true;
    };
  }, [callProtected]);

  return timeZoneId;
}
