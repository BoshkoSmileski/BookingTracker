import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError, NO_VALIDATION_ERRORS, api, errorMessage, validationErrors } from '../lib/api';
import type { ValidationErrors } from '../lib/api';
import { customFieldName } from '../lib/bookingForm';
import { BookingEventType, BookingFieldNames, FLUSH_DEBOUNCE_MS, IDLE_THRESHOLD_MS } from '../lib/config';
import type { BookingConfirmationDto, BookingPageDto, BookingSessionDto, ClientBookingEventDto } from '../lib/types';
import { useIdleTimer } from './useIdleTimer';

export interface BookingFormState {
  name: string;
  email: string;
  phone: string;
  message: string;
  selectedDate: string; // yyyy-mm-dd, straight from <input type="date">
  selectedTime: string; // HH:mm, straight from <input type="time">
  /** Answers to the page's custom fields, keyed by field id. Absent = unanswered. */
  answers: Record<string, string>;
}

const EMPTY_FORM: BookingFormState = {
  name: '',
  email: '',
  phone: '',
  message: '',
  selectedDate: '',
  selectedTime: '',
  answers: {},
};

function sessionStorageKey(slug: string) {
  return `booking-session:${slug}`;
}

/**
 * Owns the entire "record everything" lifecycle for one booking page visit:
 * starts a session, captures every field keystroke and date/time pick as its
 * own event (never just the final value), batches and flushes them to the
 * server, detects idle/active transitions, and flushes a last beacon on tab
 * close. The form component only ever calls updateField/updateDate/updateTime
 * and reads formState - it never touches the tracking machinery directly.
 */
export function useBookingSessionTracker(slug: string) {
  const [page, setPage] = useState<BookingPageDto | null>(null);
  const [session, setSession] = useState<BookingSessionDto | null>(null);
  const [confirmation, setConfirmation] = useState<BookingConfirmationDto | null>(null);
  const [formState, setFormState] = useState<BookingFormState>(EMPTY_FORM);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  // Whether the page simply does not exist, as opposed to something going
  // wrong. The two need different copy: a 404 here carries the API's own
  // "BookingPage with key 'x' was not found", which is a developer's sentence
  // and must never reach a visitor.
  const [errorIsNotFound, setErrorIsNotFound] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  // Whether the submit failed specifically because the slot went while the
  // guest was filling in the form. Kept as its own flag rather than by
  // re-testing the message text, which is the server's to word: the message
  // already tells the guest to choose another time, and this is what lets the
  // review step actually offer that instead of leaving them on a dead end.
  // 409 is ConflictException's mapping in ExceptionHandlingMiddleware.
  const [submitErrorIsConflict, setSubmitErrorIsConflict] = useState(false);
  // Per-field messages from a rejected submit, so the details step can put one
  // beside the question it is about. SubmitBookingSessionCommandHandler keys a
  // missing required answer by `custom:{fieldId}` precisely so this is possible;
  // until this existed, that care produced the two words "Validation failed".
  const [submitValidation, setSubmitValidation] = useState<ValidationErrors>(NO_VALIDATION_ERRORS);

  const sessionIdRef = useRef<string | null>(null);
  const clientSeqRef = useRef(0);
  const queueRef = useRef<ClientBookingEventDto[]>([]);
  const flushTimerRef = useRef<number | null>(null);
  const hasInitializedRef = useRef(false);

  // flush() can be triggered from several independent places at once (a
  // debounced keystroke, an immediate slot selection, idle/active toggles,
  // submit) and each one hits the network. Two mechanisms together keep
  // that safe:
  //  - flushChainRef serializes the actual requests: every flush() call
  //    chains onto whatever flush is already in flight, so there is never
  //    more than one append-events request in the air for this session at
  //    once. Since we can't add optimistic concurrency to the backend here,
  //    this is also what stops the *server* from applying two batches out of
  //    order, not just the client - by construction, request N+1 is only
  //    ever sent after request N's response has already been received.
  //  - requestVersionRef/latestAppliedVersionRef is a request-issued-order
  //    stamp: whichever request was *sent* most recently wins, even if an
  //    earlier one's response happens to resolve later (e.g. a flush
  //    response racing submit's own request, which intentionally sits
  //    outside the flush chain). A response older than the newest one
  //    already applied is discarded instead of overwriting fresher state.
  const flushChainRef = useRef<Promise<void>>(Promise.resolve());
  const requestVersionRef = useRef(0);
  const latestAppliedVersionRef = useRef(0);

  const nextSequenceNumber = useCallback(() => {
    clientSeqRef.current += 1;
    return clientSeqRef.current;
  }, []);

  const enqueue = useCallback(
    (event: Omit<ClientBookingEventDto, 'clientSequenceNumber'>) => {
      queueRef.current.push({ ...event, clientSequenceNumber: nextSequenceNumber() });
    },
    [nextSequenceNumber],
  );

  /** Applies a server response only if nothing newer has already landed - see the refs above. */
  const applySessionUpdate = useCallback((updated: BookingSessionDto, version: number) => {
    if (version < latestAppliedVersionRef.current) return;
    latestAppliedVersionRef.current = version;
    setSession(updated);
  }, []);

  const flush = useCallback(() => {
    const run = async () => {
      if (!sessionIdRef.current || queueRef.current.length === 0) return;
      const batch = queueRef.current;
      queueRef.current = [];
      const version = ++requestVersionRef.current;
      try {
        const updated = await api.appendEvents(sessionIdRef.current, batch);
        applySessionUpdate(updated, version);
      } catch {
        // Put the failed batch back in front so the next flush retries it in order.
        queueRef.current = [...batch, ...queueRef.current];
      }
    };

    // Chain rather than call directly: this is what guarantees flush()
    // never has two requests in flight at once, no matter how many places
    // called it back-to-back.
    const chained = flushChainRef.current.then(run);
    flushChainRef.current = chained;
    return chained;
  }, [applySessionUpdate]);

  const scheduleFlush = useCallback(() => {
    if (flushTimerRef.current !== null) window.clearTimeout(flushTimerRef.current);
    flushTimerRef.current = window.setTimeout(() => void flush(), FLUSH_DEBOUNCE_MS);
  }, [flush]);

  // Start (or resume) the session once. Guarded against React StrictMode's
  // double-invoked effects in development, which would otherwise create two
  // sessions for a single page visit.
  useEffect(() => {
    if (hasInitializedRef.current) return;
    hasInitializedRef.current = true;

    (async () => {
      try {
        const pageDto = await api.getBookingPage(slug);
        setPage(pageDto);

        const storageKey = sessionStorageKey(slug);
        const existingId = sessionStorage.getItem(storageKey);
        let sessionDto: BookingSessionDto | null = null;

        if (existingId) {
          try {
            const existing = await api.getSession(existingId);
            if (existing.status === 'Active') sessionDto = existing;
          } catch {
            // Stale/unknown id - fall through and start a fresh session.
          }
        }

        if (!sessionDto) {
          sessionDto = await api.startSession(slug);
          sessionStorage.setItem(storageKey, sessionDto.id);
        }

        sessionIdRef.current = sessionDto.id;
        setSession(sessionDto);
        setFormState({
          name: sessionDto.name ?? '',
          email: sessionDto.email ?? '',
          phone: sessionDto.phone ?? '',
          message: sessionDto.message ?? '',
          selectedDate: sessionDto.selectedDate ?? '',
          selectedTime: sessionDto.selectedTime?.slice(0, 5) ?? '',
          // Restored like every other field, so a reload mid-booking does not
          // silently drop answers the visitor already gave.
          answers: Object.fromEntries(sessionDto.answers.map((a) => [a.fieldId, a.value])),
        });
      } catch (e) {
        // Through errorMessage rather than `e.message`, so an unreachable API
        // says so instead of borrowing whatever wording the thrown value
        // happened to carry - the same rule every other screen follows.
        setErrorIsNotFound(e instanceof ApiError && e.status === 404);
        setError(
          errorMessage(
            e,
            'This booking page could not be opened.',
            'We could not reach the booking service. Please check your connection and try again.',
          ),
        );
      } finally {
        setLoading(false);
      }
    })();
  }, [slug]);

  const handleIdle = useCallback(() => {
    if (!sessionIdRef.current) return;
    enqueue({ eventType: BookingEventType.UserInactive, fieldName: null, newValue: null });
    void flush();
  }, [enqueue, flush]);

  const handleActive = useCallback(() => {
    if (!sessionIdRef.current) return;
    enqueue({ eventType: BookingEventType.UserActive, fieldName: null, newValue: null });
    void flush();
  }, [enqueue, flush]);

  useIdleTimer(handleIdle, handleActive, IDLE_THRESHOLD_MS, session?.status === 'Active');

  // The only reliable way to observe a closed tab: flush whatever's queued,
  // plus a BrowserClosed event, via sendBeacon on the way out.
  useEffect(() => {
    const handlePageHide = (event: PageTransitionEvent) => {
      // event.persisted means the page is going into the back/forward cache,
      // not actually closing - it may resume later, so don't record it as closed.
      if (event.persisted) return;
      if (!sessionIdRef.current) return;
      const events = [...queueRef.current];
      queueRef.current = [];
      events.push({
        eventType: BookingEventType.BrowserClosed,
        fieldName: null,
        newValue: null,
        clientSequenceNumber: nextSequenceNumber(),
      });
      api.sendBeacon(sessionIdRef.current, events);
    };

    window.addEventListener('pagehide', handlePageHide);
    return () => window.removeEventListener('pagehide', handlePageHide);
  }, [nextSequenceNumber]);

  const updateField = useCallback(
    (field: 'name' | 'email' | 'phone' | 'message', value: string) => {
      setFormState((s) => ({ ...s, [field]: value }));
      const fieldName =
        field === 'name'
          ? BookingFieldNames.Name
          : field === 'email'
            ? BookingFieldNames.Email
            : field === 'phone'
              ? BookingFieldNames.Phone
              : BookingFieldNames.Message;
      enqueue({ eventType: BookingEventType.FieldChanged, fieldName, newValue: value === '' ? null : value });
      scheduleFlush();
    },
    [enqueue, scheduleFlush],
  );

  /**
   * An answer to one of the organizer's custom fields.
   *
   * Goes through the same enqueue + scheduleFlush pair as updateField above,
   * not a flush() call of its own. The only difference
   * is the fieldName, which is what makes this an ordinary FieldChanged event
   * on the server rather than anything new.
   */
  const updateAnswer = useCallback(
    (fieldId: string, value: string) => {
      setFormState((s) => ({ ...s, answers: { ...s.answers, [fieldId]: value } }));
      enqueue({
        eventType: BookingEventType.FieldChanged,
        fieldName: customFieldName(fieldId),
        newValue: value === '' ? null : value,
      });
      scheduleFlush();
    },
    [enqueue, scheduleFlush],
  );

  /**
   * Used by the calendar slot picker: dispatches DateSelected + TimeSelected as
   * one batch (one flush). The values are organizer-local wall-clock time
   * (matching what SlotPicker hands back), exactly what BookingSession.
   * SelectedDate/SelectedTime already expect - no change to the wire format
   * either event uses.
   */
  const selectSlot = useCallback(
    (date: string, time: string) => {
      setFormState((s) => ({ ...s, selectedDate: date, selectedTime: time.slice(0, 5) }));
      enqueue({ eventType: BookingEventType.DateSelected, fieldName: null, newValue: date });
      enqueue({ eventType: BookingEventType.TimeSelected, fieldName: null, newValue: time });
      void flush();
    },
    [enqueue, flush],
  );

  /** Returns whether the submit succeeded, so callers (e.g. the booking wizard) can advance
   * to a success screen without relying on `session` state, which wouldn't be updated yet
   * in the same synchronous continuation this promise resolves in. */
  const submit = useCallback(async (): Promise<boolean> => {
    if (!sessionIdRef.current) return false;
    setSubmitting(true);
    setSubmitError(null);
    setSubmitErrorIsConflict(false);
    setSubmitValidation(NO_VALIDATION_ERRORS);
    try {
      // Drains the flush chain first, including anything scheduled/in-flight
      // from other triggers (typing debounce, idle toggles, slot selection) -
      // submit never races those, it always goes after every queued event.
      await flush();
      const version = ++requestVersionRef.current;
      const updated = await api.submitSession(sessionIdRef.current, nextSequenceNumber());
      applySessionUpdate(updated, version);
      setConfirmation(updated);
      sessionStorage.removeItem(sessionStorageKey(slug));
      return true;
    } catch (e) {
      setSubmitError(
        errorMessage(
          e,
          'We could not complete your booking. Please try again.',
          'We could not reach the booking service. Please check your connection and try again.',
        ),
      );
      setSubmitErrorIsConflict(e instanceof ApiError && e.status === 409);
      setSubmitValidation(validationErrors(e));
      return false;
    } finally {
      setSubmitting(false);
    }
  }, [applySessionUpdate, flush, nextSequenceNumber, slug]);

  useEffect(() => {
    return () => {
      if (flushTimerRef.current !== null) window.clearTimeout(flushTimerRef.current);
    };
  }, []);

  return {
    page,
    session,
    confirmation,
    formState,
    loading,
    error,
    errorIsNotFound,
    submitting,
    submitError,
    submitErrorIsConflict,
    submitValidation,
    updateField,
    updateAnswer,
    selectSlot,
    submit,
  };
}
