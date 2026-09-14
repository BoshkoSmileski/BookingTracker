import type { APIRequestContext } from '@playwright/test';
import { API_URL } from './env.mjs';

/**
 * Setting up an organizer, a booking page and (where a test needs one) a
 * question, over the real HTTP API.
 *
 * This is fixture creation, not mocking - every byte still goes through the real
 * controllers, the real MediatR pipeline and the real database. It exists so the
 * suite can stay at ~20 tests: `organizer-journey.spec.ts` performs the whole
 * setup through the browser, once, because doing it in a browser IS what that
 * test is about; every other spec starts from a page that already exists,
 * because clicking through a create form again would test nothing new and cost
 * fifteen seconds a file.
 *
 * Everything is created under a unique identifier (see `unique`), so tests never
 * collide with each other, with a previous run, or with anything left in the E2E
 * database.
 */

let counter = 0;

/** A value no other test, worker or previous run can have produced. */
export function unique(prefix: string): string {
  counter += 1;
  return `${prefix}-${Date.now().toString(36)}-${process.pid}-${counter}`;
}

export interface TestOrganizer {
  name: string;
  email: string;
  password: string;
  accessToken: string;
  /** Persisted by `AuthContext` under `bookingtracker.refreshToken`; see `signIn` in fixtures.ts. */
  refreshToken: string;
  organizerId: string;
}

export interface TestBookingPage {
  id: string;
  slug: string;
  title: string;
}

export interface Slot {
  localDate: string;
  localStartTime: string;
  localEndTime: string;
  startUtc: string;
  endUtc: string;
}

/** Fails loudly with the server's own body rather than an opaque status. */
async function ok(response: Awaited<ReturnType<APIRequestContext['post']>>, what: string) {
  if (!response.ok()) {
    throw new Error(`${what} failed: ${response.status()} ${await response.text()}`);
  }
  return response;
}

/**
 * A brand-new organizer. The password satisfies the register validator's
 * 8-character minimum; the email is unique so registration can never collide.
 */
export async function registerOrganizer(request: APIRequestContext): Promise<TestOrganizer> {
  const email = `${unique('e2e')}@bookingtracker.test`;
  const name = 'E2E Organizer';
  const password = 'E2ePassw0rd!';

  const response = await ok(
    await request.post(`${API_URL}/api/auth/register`, { data: { name, email, password } }),
    'register',
  );
  const body = await response.json();
  return {
    name,
    email,
    password,
    accessToken: body.accessToken,
    refreshToken: body.refreshToken,
    organizerId: body.organizerId,
  };
}

/**
 * A booking page for that organizer.
 *
 * `timeZoneId` is passed because it is what makes the page bookable: the very
 * first page an organizer creates also seeds a Monday-Friday 09:00-17:00
 * schedule, and this decides which clock it is created on. UTC keeps the suite's
 * expectations independent of the machine's own zone.
 */
export async function createBookingPage(
  request: APIRequestContext,
  organizer: TestOrganizer,
  overrides: Partial<{ title: string; description: string | null; durationMinutes: number }> = {},
): Promise<TestBookingPage> {
  const title = overrides.title ?? `E2E ${unique('page')}`;

  const response = await ok(
    await request.post(`${API_URL}/api/organizer/booking-pages`, {
      headers: { Authorization: `Bearer ${organizer.accessToken}` },
      data: {
        title,
        description: overrides.description ?? 'Booked by the end-to-end suite.',
        durationMinutes: overrides.durationMinutes ?? 30,
        bufferBeforeMinutes: 0,
        bufferAfterMinutes: 0,
        minNoticeMinutes: null,
        maxBookingWindowDays: null,
        maxBookingsPerDay: null,
        timeZoneId: 'UTC',
      },
    }),
    'create booking page',
  );

  const body = await response.json();
  return { id: body.id, slug: body.slug, title: body.title };
}

/**
 * A custom question on that page. `type` crosses the wire as a **string** -
 * this API registers no `JsonStringEnumConverter`, which is the exact contract
 * `CustomQuestionEnumBindingTests` was written for.
 */
export async function addFormField(
  request: APIRequestContext,
  organizer: TestOrganizer,
  pageId: string,
  field: { label: string; type?: 'ShortText' | 'LongText'; isRequired?: boolean },
): Promise<void> {
  await ok(
    await request.post(`${API_URL}/api/organizer/booking-pages/${pageId}/form-fields`, {
      headers: { Authorization: `Bearer ${organizer.accessToken}` },
      data: {
        label: field.label,
        type: field.type ?? 'ShortText',
        isRequired: field.isRequired ?? false,
      },
    }),
    'add form field',
  );
}

/** yyyy-MM-dd, from a UTC instant - the same shape the API's date keys use. */
function isoDate(date: Date): string {
  return date.toISOString().slice(0, 10);
}

/**
 * The first slot the API will actually offer, and the date it falls on.
 *
 * Asked of the server rather than computed: `SlotGenerationService` applies the
 * same-day cutoff, minimum notice and the booking window against the
 * organizer's own `TimeZoneInfo`, so any date this suite picked for itself would
 * be a second implementation of that - and the "never hardcode a near-term
 * calendar date" rule applies here for exactly the same reason it does in the
 * unit tests.
 *
 * The window starts tomorrow so the returned day is never today, whose slots can
 * disappear underneath a test as the clock passes them.
 */
export async function findFirstSlot(
  request: APIRequestContext,
  slug: string,
  { skipDays = 0 }: { skipDays?: number } = {},
): Promise<Slot> {
  const from = new Date();
  from.setUTCDate(from.getUTCDate() + 1 + skipDays);
  const to = new Date(from);
  to.setUTCDate(to.getUTCDate() + 30);

  const response = await ok(
    await request.get(
      `${API_URL}/api/booking-pages/${slug}/slots?from=${isoDate(from)}&to=${isoDate(to)}`,
    ),
    'get slots',
  );

  const slots: Slot[] = await response.json();
  if (slots.length === 0) {
    throw new Error(`No slots offered for "${slug}" between ${isoDate(from)} and ${isoDate(to)}.`);
  }
  return slots[0];
}

/**
 * A confirmed booking, made the way a guest makes one: start a session, report
 * the field changes and the slot as tracked events, then submit.
 *
 * Used by the manage-booking tests, which need a booking to exist before the
 * screen they are about is reachable at all. The wizard's own path through this
 * is covered in the browser by `public-booking.spec.ts`.
 *
 * Returns the public token - the sole credential for every manage screen.
 */
export async function bookViaApi(
  request: APIRequestContext,
  slug: string,
  guest: { name: string; email: string; message?: string },
): Promise<{ publicToken: string; bookingReference: string; slot: Slot }> {
  const slot = await findFirstSlot(request, slug);

  const started = await ok(
    await request.post(`${API_URL}/api/booking-pages/${slug}/sessions`),
    'start session',
  );
  const sessionId = (await started.json()).id;

  // A bare array, and `eventType` as a **string** - the wire shape
  // `ClientBookingEventDto` actually declares. Written from the DTO rather than
  // from the frontend's tracker on purpose: if these two ever disagree, a test
  // using this helper should fail rather than quietly agree with itself.
  await ok(
    await request.post(`${API_URL}/api/booking-sessions/${sessionId}/events`, {
      data: [
        { eventType: 'FieldChanged', fieldName: 'Name', newValue: guest.name, clientSequenceNumber: 1 },
        { eventType: 'FieldChanged', fieldName: 'Email', newValue: guest.email, clientSequenceNumber: 2 },
        // Optional, and only `responsive.spec.ts` supplies one: it needs a
        // tracked value with no break opportunity in it (a URL) to pin that the
        // organizer's timeline wraps one instead of widening the page.
        ...(guest.message
          ? [{ eventType: 'FieldChanged', fieldName: 'Message', newValue: guest.message, clientSequenceNumber: 21 }]
          : []),
        { eventType: 'DateSelected', fieldName: 'Date', newValue: slot.localDate, clientSequenceNumber: 3 },
        { eventType: 'TimeSelected', fieldName: 'Time', newValue: slot.localStartTime, clientSequenceNumber: 4 },
      ],
    }),
    'append events',
  );

  const submitted = await ok(
    await request.post(`${API_URL}/api/booking-sessions/${sessionId}/submit`, {
      data: { clientSequenceNumber: 5 },
    }),
    'submit booking',
  );
  const confirmation = await submitted.json();
  return {
    publicToken: confirmation.publicToken,
    bookingReference: confirmation.bookingReference,
    slot,
  };
}
