import { defineConfig, devices } from '@playwright/test';
import {
  API_DLL,
  API_PUBLISH_DIR,
  API_URL,
  FRONTEND_DIR,
  WEB_URL,
  apiEnvironment,
} from './e2e/support/env.mjs';

/**
 * The browser suite.
 *
 * It covers the one boundary neither of the other three suites can reach:
 * browser -> React -> HTTP -> API -> SQL Server -> HTTP -> React -> rendered UI.
 * Everything below the browser is real - a real published API, a real database,
 * real JWTs, real SignalR, real background sweepers. Nothing is mocked except
 * where a test is specifically about a *failure* (see error-states.spec.ts,
 * which aborts requests at the browser rather than taking the server down).
 *
 * Deliberately small: ~20 tests, each covering something the 750 frontend, 573
 * backend unit and 120 HTTP integration tests structurally cannot prove. A
 * business rule belongs in one of those, which are faster and far more precise.
 *
 * `npm run test:e2e` runs `scripts/e2e-prepare.mjs` first - migrations and the
 * API publish have to happen before Playwright launches `webServer`, which it
 * does *before* `globalSetup`.
 */
export default defineConfig({
  testDir: './e2e',
  // Both servers are one process each and one database, so parallel workers
  // would be sharing them anyway; a single worker is what keeps a failure
  // reproducible and the API log readable. The suite is small enough to afford it.
  workers: 1,
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: 0,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : [['list']],

  /**
   * Proves the API under test is really on the throwaway database before a
   * single test runs. Runs after `webServer`, which is exactly when the question
   * can be answered - see the file for how.
   */
  globalSetup: './e2e/global-setup.ts',

  use: {
    baseURL: WEB_URL,
    // On failure only: a screenshot is worth having when something breaks, and
    // a baseline of them is a maintenance cost with no reader.
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
    video: 'off',
  },

  projects: [
    {
      name: 'chromium',
      // 1440x900 - the width the organizer shell is actually designed for
      // (max-w-[1440px]) and above the `lg` breakpoint, so the permanent rail is
      // present rather than the drawer. Mobile tests override this per-file.
      use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 900 } },
    },
  ],

  webServer: [
    {
      // The published DLL, not `dotnet run`: a dev API running from the same
      // project holds bin/Debug open, and this way the two never contend.
      command: `dotnet "${API_DLL}"`,
      cwd: API_PUBLISH_DIR,
      // An authenticated route, unauthenticated: it answers 401, which is a
      // status Playwright accepts as "up" and which needs no seeded data to
      // exist. It also proves routing and the auth middleware are both live,
      // which a static file could not.
      url: `${API_URL}/api/organizer/booking-pages`,
      reuseExistingServer: false,
      timeout: 120_000,
      stdout: 'ignore',
      stderr: 'pipe',
      env: apiEnvironment(),
    },
    {
      command: 'npx vite --config vite.e2e.config.ts',
      cwd: FRONTEND_DIR,
      url: WEB_URL,
      reuseExistingServer: false,
      timeout: 120_000,
      stdout: 'ignore',
      stderr: 'pipe',
      env: { VITE_API_BASE_URL: API_URL },
    },
  ],
});
