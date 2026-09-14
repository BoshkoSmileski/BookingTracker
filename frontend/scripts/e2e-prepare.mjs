/**
 * Gets the machine ready for `playwright test`, and nothing else.
 *
 * Two jobs, both of which have to happen before Playwright starts anything
 * (`webServer` is launched *before* `globalSetup`, so neither can live there):
 *
 *  1. **Apply migrations to the E2E database.** `Program.cs` only migrates in
 *     Development, deliberately - and the E2E API runs as `E2E` precisely so
 *     `DevelopmentSeeder` cannot invent an organizer underneath the suite. So
 *     the schema is applied here, explicitly, exactly as the README tells a
 *     developer to do it for their own database.
 *
 *  2. **Publish the API into its own directory.** A running dev API holds
 *     `bin/Debug/net8.0/BookingTracker.Api.dll` open, so a `dotnet run` of the
 *     same project fails to build with a file lock. A Release publish writes to
 *     `obj/Release` + `artifacts/e2e-api` instead, which shares nothing with the
 *     Debug output - so the suite runs happily alongside `dotnet run` and
 *     `npm run dev`.
 *
 * Both steps are idempotent, so this is cheap on a second run.
 *
 * `--reset` additionally drops the E2E database first, for when a run needs to
 * start from genuinely nothing. Ordinary runs do not need it: every test creates
 * its own organizer under a unique email, and an organizer only ever sees their
 * own data, so leftovers from a previous run are invisible.
 */
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import {
  API_DLL,
  API_PUBLISH_DIR,
  API_TEMP_DIR,
  BACKEND_DIR,
  CONNECTION_STRING,
  E2E_DATABASE,
  E2E_JWT_SECRET,
  assertIsolatedDatabase,
} from '../e2e/support/env.mjs';

// Before anything runs, and before anything is built: the whole point of this
// file is pointing a database at a test suite, and pointing the wrong one is the
// only mistake here that costs real data.
assertIsolatedDatabase(CONNECTION_STRING);

const RESET = process.argv.includes('--reset');

/**
 * `dotnet ef` boots the app's entry point to find the DbContext, so it hits
 * Program.cs's `Jwt:Secret` fail-fast on the way past. These are the same
 * variables the API itself runs with, minus the web-host ones it has no use for.
 */
const toolEnvironment = {
  ...process.env,
  ASPNETCORE_ENVIRONMENT: 'E2E',
  ConnectionStrings__DefaultConnection: CONNECTION_STRING,
  Jwt__Issuer: 'BookingTracker.E2E',
  Jwt__Audience: 'BookingTracker.E2E',
  Jwt__Secret: E2E_JWT_SECRET,
  Email__UseSmtp: 'false',
  Email__FromEmail: 'e2e@bookingtracker.invalid',
  TMP: API_TEMP_DIR,
  TEMP: API_TEMP_DIR,
};

/** @param {string} label @param {string[]} args */
function run(label, args) {
  process.stdout.write(`e2e-prepare: ${label}\n`);
  const result = spawnSync('dotnet', args, {
    cwd: BACKEND_DIR,
    env: toolEnvironment,
    stdio: 'inherit',
    shell: process.platform === 'win32',
  });
  if (result.status !== 0) {
    process.stderr.write(`\ne2e-prepare: "${label}" failed (exit ${result.status}).\n`);
    process.exit(result.status ?? 1);
  }
}

const efArgs = [
  '--project', 'src/BookingTracker.Infrastructure',
  '--startup-project', 'src/BookingTracker.Api',
  // Release, so this never contends with a `dotnet run` holding the Debug
  // output open. Same reason as the publish below.
  '--configuration', 'Release',
];

fs.mkdirSync(API_TEMP_DIR, { recursive: true });

if (RESET) {
  run(`dropping ${E2E_DATABASE}`, ['ef', 'database', 'drop', '--force', ...efArgs]);
}

run(`migrating ${E2E_DATABASE}`, ['ef', 'database', 'update', ...efArgs]);

run('publishing the E2E API', [
  'publish',
  'src/BookingTracker.Api/BookingTracker.Api.csproj',
  '--configuration', 'Release',
  '--output', API_PUBLISH_DIR,
  '--nologo',
  '--verbosity', 'quiet',
]);

if (!fs.existsSync(API_DLL)) {
  process.stderr.write(`\ne2e-prepare: expected ${API_DLL} to exist after publish.\n`);
  process.exit(1);
}

process.stdout.write('e2e-prepare: ready.\n');
