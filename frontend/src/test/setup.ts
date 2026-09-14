import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach, vi } from 'vitest';

/**
 * Runs before every test file. Keeps three things true that jsdom does not
 * provide on its own, and that the app depends on.
 */

afterEach(() => {
  // RTL does not auto-clean when `globals: false`, so unmount here or a stray
  // component's timers/effects leak into the next test.
  cleanup();
  sessionStorage.clear();
  localStorage.clear();
});

// The booking wizard flushes tracked events on pagehide via sendBeacon, which
// jsdom has no implementation for. Stubbed as a no-op returning true so the
// unload path runs instead of throwing; tests that care assert on it directly.
if (!('sendBeacon' in navigator)) {
  Object.defineProperty(navigator, 'sendBeacon', {
    writable: true,
    value: vi.fn(() => true),
  });
}

// jsdom implements neither; several dashboard/analytics components call them
// during layout.
globalThis.ResizeObserver ??= class {
  observe() {}
  unobserve() {}
  disconnect() {}
};

// jsdom has no layout, so it implements no scrolling. The booking wizard
// scrolls the time list into view on a phone once a date is picked; without
// this, that call throws and takes the whole render with it.
if (!Element.prototype.scrollIntoView) {
  Element.prototype.scrollIntoView = vi.fn();
}

if (!window.matchMedia) {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: (query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => false,
    }),
  });
}
