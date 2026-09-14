import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError, NetworkError, api } from '../../lib/api';
import { bookingSession } from '../../test/factories';
import { useBookingSessionTracker } from '../useBookingSessionTracker';
import type { BookingPageDto, BookingSessionDto, ClientBookingEventDto } from '../../lib/types';

/**
 * REGRESSION AREA: booking tracker synchronization.
 *
 * flush() has five independent triggers (debounced typing, immediate slot
 * selection, idle, active, submit). Before the fix each could fire its own
 * unserialized request, and because the backend has no optimistic concurrency
 * token, two in-flight requests raced at the database - whichever saved last
 * silently reverted the other. Two mechanisms guard it now, and both are
 * asserted here: a promise chain (never two requests in flight) and a
 * request-version stamp (a stale response can never overwrite newer state).
 *
 * flush() itself is deliberately NOT exposed by the hook - a second call site
 * is forbidden - so everything below drives it the way the UI
 * does: updateField (debounced), selectSlot (immediate), submit (drains first).
 */

const PAGE: BookingPageDto = {
  id: '11111111-1111-1111-1111-111111111111',
  slug: 'demo-30-min-meeting',
  title: '30 Minute Meeting',
  description: null,
  organizerName: 'Demo Organizer',
  durationMinutes: 30,
  bufferBeforeMinutes: 0,
  bufferAfterMinutes: 0,
  meetingProvider: 'None',
  timeZoneId: 'Europe/Skopje',
  instructions: [],
  formFields: [],
};

const ACTIVE_SESSION = bookingSession({ status: 'Active', name: null, email: null, submittedAt: null });

const CONFIRMATION = {
  ...bookingSession(), cancelledAt: null, rescheduledAt: null, bookingReference: 'ABCD-1234', publicToken: 'tok',
  guestConfirmationQueued: true,
};

async function mountTracker() {
  vi.spyOn(api, 'getBookingPage').mockResolvedValue(PAGE);
  vi.spyOn(api, 'startSession').mockResolvedValue(ACTIVE_SESSION);

  const rendered = renderHook(() => useBookingSessionTracker('demo-30-min-meeting'));
  await waitFor(() => expect(rendered.result.current.loading).toBe(false));
  return rendered;
}

describe('useBookingSessionTracker', () => {
  beforeEach(() => {
    sessionStorage.clear();
  });

  describe('session start', () => {
    it('loads the page and starts a session', async () => {
      const { result } = await mountTracker();

      expect(result.current.page?.title).toBe('30 Minute Meeting');
      expect(result.current.session?.id).toBe(ACTIVE_SESSION.id);
      expect(result.current.error).toBeNull();
    });

    it('persists the session id so a reload resumes rather than restarting', async () => {
      await mountTracker();

      expect(sessionStorage.getItem('booking-session:demo-30-min-meeting')).toBe(ACTIVE_SESSION.id);
    });

    it('resumes an existing Active session instead of starting a new one', async () => {
      sessionStorage.setItem('booking-session:demo-30-min-meeting', ACTIVE_SESSION.id);
      vi.spyOn(api, 'getBookingPage').mockResolvedValue(PAGE);
      const getSession = vi.spyOn(api, 'getSession').mockResolvedValue(ACTIVE_SESSION);
      const startSession = vi.spyOn(api, 'startSession').mockResolvedValue(ACTIVE_SESSION);

      const { result } = renderHook(() => useBookingSessionTracker('demo-30-min-meeting'));
      await waitFor(() => expect(result.current.loading).toBe(false));

      expect(getSession).toHaveBeenCalledWith(ACTIVE_SESSION.id);
      expect(startSession).not.toHaveBeenCalled();
    });

    it('starts a fresh session when the stored one is no longer Active', async () => {
      sessionStorage.setItem('booking-session:demo-30-min-meeting', 'stale-id');
      vi.spyOn(api, 'getBookingPage').mockResolvedValue(PAGE);
      vi.spyOn(api, 'getSession').mockResolvedValue(bookingSession({ status: 'Submitted' }));
      const startSession = vi.spyOn(api, 'startSession').mockResolvedValue(ACTIVE_SESSION);

      const { result } = renderHook(() => useBookingSessionTracker('demo-30-min-meeting'));
      await waitFor(() => expect(result.current.loading).toBe(false));

      expect(startSession).toHaveBeenCalled();
    });

    it('starts a fresh session when the stored id no longer exists', async () => {
      sessionStorage.setItem('booking-session:demo-30-min-meeting', 'gone');
      vi.spyOn(api, 'getBookingPage').mockResolvedValue(PAGE);
      vi.spyOn(api, 'getSession').mockRejectedValue(new Error('404'));
      const startSession = vi.spyOn(api, 'startSession').mockResolvedValue(ACTIVE_SESSION);

      const { result } = renderHook(() => useBookingSessionTracker('demo-30-min-meeting'));
      await waitFor(() => expect(result.current.loading).toBe(false));

      expect(startSession).toHaveBeenCalled();
    });

    it('surfaces the server\'s own message and stops loading when the page cannot be loaded', async () => {
      // A real `ApiError`, which is what the client throws for a rejected
      // request - the load path goes through `errorMessage` like every other
      // screen rather than reading `e.message` off whatever was thrown.
      vi.spyOn(api, 'getBookingPage').mockRejectedValue(
        new ApiError(404, { title: 'Booking page not found.' }),
      );

      const { result } = renderHook(() => useBookingSessionTracker('nope'));
      await waitFor(() => expect(result.current.loading).toBe(false));

      expect(result.current.error).toBe('Booking page not found.');
      expect(result.current.session).toBeNull();
    });

    it('reports an unreachable API as connectivity, not as a missing booking page', async () => {
      // The split that exists so a call site cannot report an outage in its own
      // domain wording. A guest told "this booking page could not be opened"
      // when the API is simply down goes looking for the wrong problem.
      vi.spyOn(api, 'getBookingPage').mockRejectedValue(new NetworkError());

      const { result } = renderHook(() => useBookingSessionTracker('demo'));
      await waitFor(() => expect(result.current.loading).toBe(false));

      expect(result.current.error).toMatch(/could not reach the booking service/i);
    });
  });

  describe('flush serialization', () => {
    it('never has two append-events requests in flight, even when a trigger fires mid-request', async () => {
      // The actual race: a second trigger arriving while the first request is still
      // open. Without the chain both would be sent, and the slower response would
      // silently revert the faster one server-side.
      const { result } = await mountTracker();

      let inFlight = 0;
      let maxInFlight = 0;
      const appendEvents = vi.spyOn(api, 'appendEvents').mockImplementation(async () => {
        inFlight += 1;
        maxInFlight = Math.max(maxInFlight, inFlight);
        await new Promise((r) => setTimeout(r, 40));
        inFlight -= 1;
        return ACTIVE_SESSION;
      });

      await act(async () => {
        result.current.selectSlot('2026-08-20', '09:00');
        // Wait until request 1 is actually open, then fire more triggers into it.
        await waitFor(() => expect(appendEvents).toHaveBeenCalledTimes(1));
        result.current.selectSlot('2026-08-21', '10:00');
        result.current.updateField('name', 'Jane');
        await waitFor(() => expect(appendEvents).toHaveBeenCalledTimes(2));
      });

      expect(maxInFlight).toBe(1);
    });

    it('coalesces triggers queued before a request starts into a single batch', async () => {
      // Consequence of the chain worth pinning: three rapid selections produce one
      // request carrying all six events, not three requests.
      const { result } = await mountTracker();
      const appendEvents = vi.spyOn(api, 'appendEvents').mockResolvedValue(ACTIVE_SESSION);

      await act(async () => {
        result.current.selectSlot('2026-08-20', '09:00');
        result.current.selectSlot('2026-08-21', '10:00');
        result.current.selectSlot('2026-08-22', '11:00');
        await waitFor(() => expect(appendEvents).toHaveBeenCalledTimes(1));
      });

      expect(appendEvents.mock.calls[0][1]).toHaveLength(6);
    });

    it('requeues a failed batch so no tracked event is ever lost', async () => {
      const { result } = await mountTracker();

      const sent: ClientBookingEventDto[][] = [];
      const appendEvents = vi.spyOn(api, 'appendEvents')
        .mockImplementationOnce(async () => { throw new Error('network down'); })
        .mockImplementation(async (_id, events) => { sent.push(events); return ACTIVE_SESSION; });

      await act(async () => {
        result.current.selectSlot('2026-08-20', '09:00');
        await waitFor(() => expect(appendEvents).toHaveBeenCalledTimes(1));
      });
      // First attempt failed; the batch must still be pending, not dropped.
      await act(async () => {
        result.current.selectSlot('2026-08-21', '10:00');
        await waitFor(() => expect(sent.length).toBe(1));
      });

      const values = sent.flat().map((e) => e.newValue);
      expect(values).toContain('2026-08-20');
      expect(values).toContain('2026-08-21');
    });

    it('preserves client sequence order across a retry', async () => {
      const { result } = await mountTracker();

      const sent: ClientBookingEventDto[][] = [];
      const appendEvents = vi.spyOn(api, 'appendEvents')
        .mockImplementationOnce(async () => { throw new Error('network down'); })
        .mockImplementation(async (_id, events) => { sent.push(events); return ACTIVE_SESSION; });

      await act(async () => {
        result.current.selectSlot('2026-08-20', '09:00');
        await waitFor(() => expect(appendEvents).toHaveBeenCalledTimes(1));
      });
      await act(async () => {
        result.current.selectSlot('2026-08-21', '10:00');
        await waitFor(() => expect(sent.length).toBe(1));
      });

      const seqs = sent.flat().map((e) => e.clientSequenceNumber);
      expect(seqs).toEqual([...seqs].sort((a, b) => a - b));
    });

    it('sends nothing when there is nothing queued', async () => {
      const { result } = await mountTracker();
      const appendEvents = vi.spyOn(api, 'appendEvents').mockResolvedValue(ACTIVE_SESSION);
      vi.spyOn(api, 'submitSession').mockResolvedValue(CONFIRMATION);

      // submit() drains the chain first, but an empty queue must not produce a request.
      await act(async () => { await result.current.submit(); });

      expect(appendEvents).not.toHaveBeenCalled();
    });
  });

  describe('response versioning', () => {
    it('discards a stale response instead of overwriting newer session state', async () => {
      // The guard that protects submit(): its request sits outside the flush chain,
      // so a straggling flush response can resolve after it and must not win.
      const { result } = await mountTracker();

      const slow: BookingSessionDto = { ...ACTIVE_SESSION, name: 'STALE' };
      const fast: BookingSessionDto = { ...ACTIVE_SESSION, name: 'FRESH' };

      const appendEvents = vi.spyOn(api, 'appendEvents')
        .mockImplementationOnce(async () => { await new Promise((r) => setTimeout(r, 40)); return slow; })
        .mockImplementationOnce(async () => fast);

      await act(async () => {
        result.current.selectSlot('2026-08-20', '09:00');
        // Queue the second batch only once the first request is open, so it becomes
        // its own (later-versioned) request rather than being coalesced into the first.
        await waitFor(() => expect(appendEvents).toHaveBeenCalledTimes(1));
        result.current.selectSlot('2026-08-21', '10:00');
        await waitFor(() => expect(appendEvents).toHaveBeenCalledTimes(2));
      });

      await waitFor(() => expect(result.current.session?.name).toBe('FRESH'));
    });
  });

  describe('field tracking', () => {
    it('records every keystroke as its own event, not just the final value', async () => {
      const { result } = await mountTracker();
      const appendEvents = vi.spyOn(api, 'appendEvents').mockResolvedValue(ACTIVE_SESSION);

      await act(async () => {
        result.current.updateField('email', 'j');
        result.current.updateField('email', 'ja');
        result.current.updateField('email', 'jane@example.com');
        // selectSlot flushes immediately, draining the queued keystrokes with it.
        result.current.selectSlot('2026-08-20', '09:00');
        await waitFor(() => expect(appendEvents).toHaveBeenCalled());
      });

      const events = appendEvents.mock.calls[0][1];
      const emailEvents = events.filter((e) => e.fieldName === 'Email');
      expect(emailEvents).toHaveLength(3);
      expect(emailEvents.map((e) => e.newValue)).toEqual(['j', 'ja', 'jane@example.com']);
    });

    it('exposes the typed value immediately, before any flush', async () => {
      const { result } = await mountTracker();

      act(() => { result.current.updateField('name', 'Jane'); });

      expect(result.current.formState.name).toBe('Jane');
    });

    it('records a cleared field as null rather than an empty string', async () => {
      const { result } = await mountTracker();
      const appendEvents = vi.spyOn(api, 'appendEvents').mockResolvedValue(ACTIVE_SESSION);

      await act(async () => {
        result.current.updateField('phone', '');
        result.current.selectSlot('2026-08-20', '09:00');
        await waitFor(() => expect(appendEvents).toHaveBeenCalled());
      });

      const phoneEvent = appendEvents.mock.calls[0][1].find((e) => e.fieldName === 'Phone');
      expect(phoneEvent?.newValue).toBeNull();
    });

    it('debounces typing into a single request rather than one per keystroke', async () => {
      const { result } = await mountTracker();
      const appendEvents = vi.spyOn(api, 'appendEvents').mockResolvedValue(ACTIVE_SESSION);

      act(() => {
        result.current.updateField('name', 'J');
        result.current.updateField('name', 'Ja');
        result.current.updateField('name', 'Jane');
      });

      await waitFor(() => expect(appendEvents).toHaveBeenCalledTimes(1), { timeout: 2000 });
      expect(appendEvents.mock.calls[0][1]).toHaveLength(3);
    });

    it('sends date and time as one batch when a slot is picked', async () => {
      const { result } = await mountTracker();
      const appendEvents = vi.spyOn(api, 'appendEvents').mockResolvedValue(ACTIVE_SESSION);

      await act(async () => {
        result.current.selectSlot('2026-08-20', '09:00');
        await waitFor(() => expect(appendEvents).toHaveBeenCalledTimes(1));
      });

      expect(appendEvents.mock.calls[0][1].map((e) => e.eventType)).toEqual(['DateSelected', 'TimeSelected']);
      expect(result.current.formState.selectedDate).toBe('2026-08-20');
      expect(result.current.formState.selectedTime).toBe('09:00');
    });
  });

  describe('custom field answers', () => {
    const FIELD_ID = '7c9e6679-7425-40de-944b-e07fc1f90ae7';

    it('reports an answer as an ordinary FieldChanged event keyed by field id', async () => {
      // The load-bearing claim of the whole feature: an answer is not a new
      // kind of event, which is why no BookingEventType was added.
      const { result } = await mountTracker();
      const appendEvents = vi.spyOn(api, 'appendEvents').mockResolvedValue(ACTIVE_SESSION);

      await act(async () => {
        result.current.updateAnswer(FIELD_ID, 'Acme Ltd');
        result.current.selectSlot('2026-08-20', '09:00');
        await waitFor(() => expect(appendEvents).toHaveBeenCalled());
      });

      const answerEvent = appendEvents.mock.calls[0][1].find((e) => e.fieldName === `custom:${FIELD_ID}`);
      expect(answerEvent?.eventType).toBe('FieldChanged');
      expect(answerEvent?.newValue).toBe('Acme Ltd');
    });

    it('records every keystroke of an answer, like any other field', async () => {
      const { result } = await mountTracker();
      const appendEvents = vi.spyOn(api, 'appendEvents').mockResolvedValue(ACTIVE_SESSION);

      act(() => {
        result.current.updateAnswer(FIELD_ID, 'A');
        result.current.updateAnswer(FIELD_ID, 'Ac');
        result.current.updateAnswer(FIELD_ID, 'Acme');
      });

      // Debounced into one request rather than one per keystroke - the same
      // path updateField takes, which is what the flush serialization requires.
      await waitFor(() => expect(appendEvents).toHaveBeenCalledTimes(1), { timeout: 2000 });
      expect(appendEvents.mock.calls[0][1].map((e) => e.newValue)).toEqual(['A', 'Ac', 'Acme']);
    });

    it('records a cleared answer as null rather than an empty string', async () => {
      const { result } = await mountTracker();
      const appendEvents = vi.spyOn(api, 'appendEvents').mockResolvedValue(ACTIVE_SESSION);

      await act(async () => {
        result.current.updateAnswer(FIELD_ID, '');
        result.current.selectSlot('2026-08-20', '09:00');
        await waitFor(() => expect(appendEvents).toHaveBeenCalled());
      });

      const answerEvent = appendEvents.mock.calls[0][1].find((e) => e.fieldName === `custom:${FIELD_ID}`);
      expect(answerEvent?.newValue).toBeNull();
    });

    it('exposes the typed answer immediately, before any flush', async () => {
      const { result } = await mountTracker();

      act(() => { result.current.updateAnswer(FIELD_ID, 'Acme'); });

      expect(result.current.formState.answers[FIELD_ID]).toBe('Acme');
    });

    it('restores answers from a resumed session so a reload does not lose them', async () => {
      sessionStorage.setItem('booking-session:demo-30-min-meeting', ACTIVE_SESSION.id);
      vi.spyOn(api, 'getBookingPage').mockResolvedValue(PAGE);
      vi.spyOn(api, 'getSession').mockResolvedValue({
        ...ACTIVE_SESSION,
        answers: [{ fieldId: FIELD_ID, value: 'Acme Ltd' }],
      });

      const { result } = renderHook(() => useBookingSessionTracker('demo-30-min-meeting'));
      await waitFor(() => expect(result.current.loading).toBe(false));

      expect(result.current.formState.answers[FIELD_ID]).toBe('Acme Ltd');
    });
  });

  describe('submit', () => {
    it('drains queued events before submitting, then reports the confirmation', async () => {
      const { result } = await mountTracker();
      const appendEvents = vi.spyOn(api, 'appendEvents').mockResolvedValue(ACTIVE_SESSION);
      const submitSession = vi.spyOn(api, 'submitSession').mockResolvedValue(CONFIRMATION);

      let ok = false;
      await act(async () => {
        result.current.updateField('name', 'Jane');
        ok = await result.current.submit();
      });

      expect(ok).toBe(true);
      expect(appendEvents).toHaveBeenCalled();
      expect(appendEvents.mock.invocationCallOrder[0]).toBeLessThan(submitSession.mock.invocationCallOrder[0]);
      expect(result.current.confirmation?.bookingReference).toBe('ABCD-1234');
    });

    it('surfaces the server’s message and stays unconfirmed when submit is rejected', async () => {
      const { result } = await mountTracker();
      vi.spyOn(api, 'submitSession').mockRejectedValue(new ApiError(409, { title: 'That slot was just taken.' }));

      let ok = true;
      await act(async () => { ok = await result.current.submit(); });

      expect(ok).toBe(false);
      expect(result.current.submitError).toBe('That slot was just taken.');
      expect(result.current.confirmation).toBeNull();
    });

    it('clears the stored session id after a successful submit', async () => {
      const { result } = await mountTracker();
      vi.spyOn(api, 'submitSession').mockResolvedValue(CONFIRMATION);

      await act(async () => { await result.current.submit(); });

      expect(sessionStorage.getItem('booking-session:demo-30-min-meeting')).toBeNull();
    });

    it('keeps the stored session id when submit fails, so the visitor can retry', async () => {
      const { result } = await mountTracker();
      vi.spyOn(api, 'submitSession').mockRejectedValue(new ApiError(409, { title: 'Taken.' }));

      await act(async () => { await result.current.submit(); });

      expect(sessionStorage.getItem('booking-session:demo-30-min-meeting')).toBe(ACTIVE_SESSION.id);
    });
  });

  describe('cleanup', () => {
    it('flushes a final beacon on pagehide', async () => {
      const beacon = vi.spyOn(api, 'sendBeacon').mockReturnValue(true);
      const { result } = await mountTracker();

      act(() => { result.current.updateField('name', 'Jane'); });
      act(() => { window.dispatchEvent(Object.assign(new Event('pagehide'), { persisted: false })); });

      expect(beacon).toHaveBeenCalledTimes(1);
      const [, events] = beacon.mock.calls[0];
      expect(events.map((e) => e.eventType)).toContain('BrowserClosed');
    });

    it('does not record a browser close when the page only enters the back/forward cache', async () => {
      const beacon = vi.spyOn(api, 'sendBeacon').mockReturnValue(true);
      await mountTracker();

      act(() => { window.dispatchEvent(Object.assign(new Event('pagehide'), { persisted: true })); });

      expect(beacon).not.toHaveBeenCalled();
    });

    it('removes its pagehide listener on unmount', async () => {
      const beacon = vi.spyOn(api, 'sendBeacon').mockReturnValue(true);
      const { unmount } = await mountTracker();

      unmount();
      act(() => { window.dispatchEvent(Object.assign(new Event('pagehide'), { persisted: false })); });

      expect(beacon).not.toHaveBeenCalled();
    });
  });
});
