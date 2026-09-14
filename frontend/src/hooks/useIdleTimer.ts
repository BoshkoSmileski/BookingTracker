import { useEffect, useRef } from 'react';

const ACTIVITY_EVENTS = ['mousemove', 'keydown', 'touchstart', 'scroll', 'click'] as const;

/**
 * Fires onIdle after `timeoutMs` of no user activity anywhere on the page, and
 * onActive the moment activity resumes after an idle period. Used to drive the
 * UserInactive / UserActive tracking events - not tied to any single input.
 */
export function useIdleTimer(
  onIdle: () => void,
  onActive: () => void,
  timeoutMs: number,
  enabled: boolean,
): void {
  const isIdleRef = useRef(false);
  const timerRef = useRef<number | null>(null);

  useEffect(() => {
    if (!enabled) return;

    const resetTimer = () => {
      if (isIdleRef.current) {
        isIdleRef.current = false;
        onActive();
      }
      if (timerRef.current !== null) window.clearTimeout(timerRef.current);
      timerRef.current = window.setTimeout(() => {
        isIdleRef.current = true;
        onIdle();
      }, timeoutMs);
    };

    resetTimer();
    ACTIVITY_EVENTS.forEach((evt) => window.addEventListener(evt, resetTimer));

    return () => {
      ACTIVITY_EVENTS.forEach((evt) => window.removeEventListener(evt, resetTimer));
      if (timerRef.current !== null) window.clearTimeout(timerRef.current);
    };
  }, [enabled, timeoutMs, onIdle, onActive]);
}
