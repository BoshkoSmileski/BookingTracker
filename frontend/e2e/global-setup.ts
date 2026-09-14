import {
  API_URL,
  CONNECTION_STRING,
  DEMO_ORGANIZER,
  DEV_DATABASE,
  E2E_DATABASE,
  assertIsolatedDatabase,
} from './support/env.mjs';

/**
 * Refuses to run the suite unless the API under test is demonstrably on the
 * throwaway database.
 *
 * The static check (`assertIsolatedDatabase`) only proves what *we* configured.
 * It cannot prove what the API actually did with it - and the API's own
 * `appsettings.json` points at the developer's `BookingTracker`, so a dropped
 * environment variable would leave every test writing organizers, booking pages
 * and bookings into real data while passing perfectly.
 *
 * So there is a second, independent check, and it needs no database driver:
 *
 *   `DevelopmentSeeder` runs **only** in the Development environment, and the
 *   E2E API runs as `E2E`. The demo organizer therefore cannot exist in the E2E
 *   database - but it certainly does exist in the developer's. If this login
 *   succeeds, the API is on the wrong database, and the run is aborted before a
 *   single row is written.
 *
 * Runs after `webServer` (Playwright starts servers first), which is exactly
 * when the question becomes answerable.
 */
export default async function globalSetup(): Promise<void> {
  assertIsolatedDatabase(CONNECTION_STRING);

  const response = await fetch(`${API_URL}/api/auth/login`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(DEMO_ORGANIZER),
  });

  if (response.ok) {
    throw new Error(
      `E2E aborted: the demo organizer (${DEMO_ORGANIZER.email}) signed in successfully against ` +
        `${API_URL}. DevelopmentSeeder only runs in the Development environment, so that account ` +
        `cannot exist in ${E2E_DATABASE} - which means the API is talking to the development ` +
        `database "${DEV_DATABASE}". Check that the E2E environment variables reached the API ` +
        `process before running again.`,
    );
  }

  if (response.status !== 401) {
    throw new Error(
      `E2E aborted: expected 401 from ${API_URL}/api/auth/login for a non-existent account, got ` +
        `${response.status}. The API is up but not behaving as expected; the run would be ` +
        `measuring the wrong thing.`,
    );
  }
}
