/**
 * The E2E environment, in one place.
 *
 * Everything here exists to keep a browser test run away from the machine's
 * real development setup. Three separate things had to be moved, not one:
 *
 *  - **The database.** A dedicated `BookingTracker_E2E`, never the dev
 *    `BookingTracker`. `assertIsolatedDatabase` below refuses to run against
 *    anything else, and `global-setup.mjs` proves the running API is really on
 *    it a second, independent way.
 *  - **The ports.** 5299/5199 rather than 5216/5173, so a dev API and a dev Vite
 *    can stay running while the suite executes. That is not only convenience:
 *    a running dev API holds `bin/Debug/net8.0` open, which is why the E2E API
 *    is a Release *publish* into its own directory (`scripts/e2e-prepare.mjs`)
 *    rather than a `dotnet run` of the same project.
 *  - **`%TEMP%`.** `FileSystemEmailService` writes to
 *    `%TEMP%\BookingTracker\sent-emails` and Data Protection persists its key
 *    ring to `%TEMP%\BookingTracker\dataprotection-keys`, both hardcoded
 *    relative to `Path.GetTempPath()`. Pointing the child process's TMP/TEMP at
 *    a directory of our own keeps a test run out of the folders the real app
 *    uses - no production change needed.
 *
 * Configuration reaches the API as environment variables rather than an
 * `appsettings.E2E.json`, for the reason `BookingTrackerApiFactory` documents:
 * environment variables are read by `WebApplication.CreateBuilder` itself, and
 * they outrank User Secrets - which matters here, because this machine's User
 * Secrets carry real SMTP credentials that would otherwise send real email.
 *
 * Plain `.mjs` rather than TypeScript on purpose: `scripts/e2e-prepare.mjs` runs
 * under bare Node before Playwright exists in the picture, and the connection
 * string is the one value that must not have a second copy anywhere.
 */
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));

/** `frontend/` */
export const FRONTEND_DIR = path.resolve(here, '..', '..');
/** repository root */
export const REPO_ROOT = path.resolve(FRONTEND_DIR, '..');
export const BACKEND_DIR = path.join(REPO_ROOT, 'backend');

/** Where `scripts/e2e-prepare.mjs` publishes the API, and where Playwright starts it from. */
export const API_PUBLISH_DIR = path.join(BACKEND_DIR, 'artifacts', 'e2e-api');
export const API_DLL = path.join(API_PUBLISH_DIR, 'BookingTracker.Api.dll');

/** Redirected TMP/TEMP for the API process. Emails and Data Protection keys land here. */
export const API_TEMP_DIR = path.join(BACKEND_DIR, 'artifacts', 'e2e-temp');

export const API_PORT = 5299;
export const WEB_PORT = 5199;
export const API_URL = `http://127.0.0.1:${API_PORT}`;
export const WEB_URL = `http://127.0.0.1:${WEB_PORT}`;

/** The development database this suite must never touch. */
export const DEV_DATABASE = 'BookingTracker';
export const E2E_DATABASE = 'BookingTracker_E2E';

export const CONNECTION_STRING =
  process.env.E2E_CONNECTION_STRING ??
  `Server=localhost\\SQLEXPRESS;Database=${E2E_DATABASE};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True`;

/**
 * Not a real secret, and never used outside this suite - it signs tokens issued
 * by an API talking to a throwaway database. It exists at all because
 * `Program.cs` fails fast without `Jwt:Secret`, deliberately, and User Secrets
 * are not loaded outside the Development environment.
 */
export const E2E_JWT_SECRET =
  'e2e-only-signing-key-not-a-production-secret-0123456789abcdef';

/**
 * The credentials `DevelopmentSeeder` creates, mirrored from `lib/devDemo.ts`.
 *
 * Used for exactly one thing: `global-setup.mjs` asserts this login **fails**
 * against the E2E API. The seeder only runs in the Development environment, so
 * a successful login here would mean the API is talking to the developer's own
 * database - which is the single failure this suite must never have.
 */
export const DEMO_ORGANIZER = {
  email: 'organizer@example.com',
  password: 'Passw0rd!',
};

/**
 * Refuses a connection string that does not name the E2E database.
 *
 * Belt and braces against the one mistake that would actually cost something:
 * the API's own `appsettings.json` points at `BookingTracker`, so a dropped or
 * misspelled environment variable would silently run the whole suite against
 * real data.
 *
 * @param {string} connectionString
 */
export function assertIsolatedDatabase(connectionString) {
  const database = /Database=([^;]+)/i.exec(connectionString)?.[1]?.trim();
  if (database !== E2E_DATABASE) {
    throw new Error(
      `E2E refuses to run against database "${database ?? '(none)'}". The connection string ` +
        `must name "${E2E_DATABASE}" - never the development database "${DEV_DATABASE}". ` +
        `Check E2E_CONNECTION_STRING.`,
    );
  }
}

/**
 * Everything the E2E API process needs, on top of the ambient environment.
 *
 * `__` is `IConfiguration`'s cross-platform section separator, so these are the
 * same keys the app documents for production - the same mechanism, pointed at
 * throwaway values.
 *
 * @returns {Record<string, string>}
 */
export function apiEnvironment() {
  assertIsolatedDatabase(CONNECTION_STRING);

  return {
    // Not "Development": that branch of Program.cs migrates and then runs
    // DevelopmentSeeder, which would invent an organizer and a booking page
    // underneath the suite. Migrations are applied explicitly instead, by
    // scripts/e2e-prepare.mjs, so no test-only branch is needed in Program.cs.
    ASPNETCORE_ENVIRONMENT: 'E2E',
    ASPNETCORE_URLS: API_URL,

    // EF Core logs every command at Information, and four sweepers poll on
    // timers, so the default level buries a genuine exception under thousands
    // of SELECTs. Warning keeps anything actually worth reading - a failed
    // sweep, an unhandled request - visible on stderr.
    Logging__LogLevel__Default: 'Warning',
    Logging__LogLevel__Microsoft: 'Warning',
    'Logging__LogLevel__Microsoft.EntityFrameworkCore': 'Warning',

    ConnectionStrings__DefaultConnection: CONNECTION_STRING,

    Jwt__Issuer: 'BookingTracker.E2E',
    Jwt__Audience: 'BookingTracker.E2E',
    Jwt__Secret: E2E_JWT_SECRET,
    Jwt__AccessTokenMinutes: '15',
    Jwt__RefreshTokenDays: '30',

    // False selects FileSystemEmailService, so nothing is transmitted. Set
    // explicitly because this machine's User Secrets switch real SMTP on - and
    // environment variables outrank User Secrets, which is what makes this safe.
    Email__UseSmtp: 'false',
    Email__FromEmail: 'e2e@bookingtracker.invalid',
    Email__FromName: 'BookingTracker E2E',

    // Every anonymous limiter partitions by client IP, and every request in this
    // suite arrives from one. Raised rather than removed, so the limiter
    // middleware still runs on the real request path.
    RateLimiting__Authentication__PermitLimit: '10000',
    RateLimiting__Booking__PermitLimit: '10000',

    Frontend__BaseUrl: WEB_URL,
    Cors__AllowedOrigins__0: WEB_URL,

    // Left empty deliberately: any code path that genuinely tried to reach
    // Google would fail loudly rather than quietly appear to work. This suite
    // never creates a CalendarConnection, so none is reached.
    GoogleCalendar__ClientId: '',
    GoogleCalendar__ClientSecret: '',
    GoogleCalendar__RedirectUri: `${API_URL}/api/calendar/google/callback`,

    // Path.GetTempPath() reads TMP first, then TEMP on Windows, and TMPDIR on
    // Unix. Redirecting all three keeps the dev-mode email sink and the Data
    // Protection key ring out of the shared BookingTracker temp folder the
    // developer's own app uses - and keeps that true on a Linux CI runner, where
    // TMP/TEMP alone would be ignored and everything would land in /tmp.
    TMP: API_TEMP_DIR,
    TEMP: API_TEMP_DIR,
    TMPDIR: API_TEMP_DIR,
  };
}
