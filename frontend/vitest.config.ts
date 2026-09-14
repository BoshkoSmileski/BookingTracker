import { defineConfig, mergeConfig } from 'vitest/config';
// Explicit extension: Vite's native config loader (the future default) does not
// resolve extensionless relative imports here.
import viteConfig from './vite.config.ts';

/**
 * Test config kept separate from vite.config.ts rather than adding a `test`
 * block there: the production build config stays exactly as it was, and the two
 * can't drift into each other. Plugins/aliases are inherited via mergeConfig so
 * tests resolve modules exactly the way the app does.
 */
export default mergeConfig(
  viteConfig,
  defineConfig({
    test: {
      environment: 'jsdom',
      // Explicit imports (`import { describe, it } from 'vitest'`) instead of
      // globals: nothing is injected into the type namespace, so test files
      // typecheck under the same tsconfig as production code.
      globals: false,
      setupFiles: ['./src/test/setup.ts'],
      include: ['src/**/*.test.{ts,tsx}'],
      restoreMocks: true,
      css: false,
      coverage: {
        provider: 'v8',
        reporter: ['text-summary', 'html'],
        include: ['src/**/*.{ts,tsx}'],
        exclude: [
          'src/**/*.test.{ts,tsx}',
          'src/test/**',
          'src/main.tsx',
          'src/vite-env.d.ts',
          // Type-only module: erased at compile time, so it has no runtime lines
          // to cover and would otherwise report a misleading 0%.
          'src/lib/types.ts',
        ],
      },
    },
  }),
);
