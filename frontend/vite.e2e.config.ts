import { defineConfig, mergeConfig } from 'vite';
// Explicit extension, matching vitest.config.ts: Vite's native config loader
// does not resolve extensionless relative imports here.
import viteConfig from './vite.config.ts';
import { WEB_PORT } from './e2e/support/env.mjs';

/**
 * The dev server the Playwright suite drives.
 *
 * A third config rather than flags on the existing one, for two reasons that are
 * both about coexisting with the developer's own `npm run dev`:
 *
 *  - **Its own port** (5199, not 5173), and `strictPort` so a clash fails loudly
 *    instead of silently landing on 5174 and testing whatever is on it.
 *  - **Its own dependency cache.** Two Vite dev servers over one project
 *    otherwise share `node_modules/.vite`, and each will happily re-optimise
 *    dependencies out from under the other - which surfaces as sporadic
 *    "Outdated Optimize Dep" reloads, i.e. exactly the kind of flake an E2E
 *    suite must not have.
 *
 * The **dev** server rather than `vite preview` over a build, deliberately: React
 * StrictMode only double-invokes effects in a development build, and that double
 * invocation is what the shared-refresh fix exists to survive. Testing a
 * production bundle would quietly skip the very path these tests most want to
 * cover.
 *
 * `VITE_API_BASE_URL` is supplied by playwright.config.ts, so the app talks to
 * the isolated E2E API rather than to whatever is on the default port.
 */
export default mergeConfig(
  viteConfig,
  defineConfig({
    cacheDir: 'node_modules/.vite-e2e',
    server: {
      port: WEB_PORT,
      strictPort: true,
      // Explicitly IPv4. Vite's default host is `localhost`, which on Windows
      // resolves to `::1` first - so the dev server binds IPv6 only and every
      // `http://127.0.0.1:5199` request (Playwright's readiness probe, and the
      // baseURL the tests navigate to) is refused. Naming the loopback address
      // also keeps the browser's Origin byte-identical to `Cors:AllowedOrigins`,
      // which the API checks exactly.
      host: '127.0.0.1',
    },
  }),
);
