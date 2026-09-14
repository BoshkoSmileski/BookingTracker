import { render, type RenderOptions, type RenderResult } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactElement, ReactNode } from 'react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';

/**
 * The one place tests get a rendered component. Every page in this app uses
 * react-router (Link, useParams), so a router is always supplied - without it
 * every page test would fail on a routing hook rather than on the thing it is
 * testing.
 *
 * Also returns a `user` from user-event, already set up, so tests never forget
 * `userEvent.setup()` (which is what makes pointer-events and focus behave
 * realistically instead of firing raw synthetic events).
 */

export interface RenderWithProvidersOptions extends Omit<RenderOptions, 'wrapper'> {
  /**
   * URL the memory router starts at, e.g. '/dashboard/abc/settings/notifications'.
   * An object form plants router `state` too, for the screens that read one -
   * the create form hands the page it just made through it.
   */
  route?: string | { pathname: string; search?: string; state?: unknown };
  /** Route pattern to mount the element under, when the component reads useParams. */
  path?: string;
}

export interface RenderWithProvidersResult extends RenderResult {
  user: ReturnType<typeof userEvent.setup>;
}

export function renderWithProviders(
  ui: ReactElement,
  { route = '/', path, ...options }: RenderWithProvidersOptions = {},
): RenderWithProvidersResult {
  const user = userEvent.setup();

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <MemoryRouter initialEntries={[route]}>
        {/* Only build a Routes tree when the component needs URL params -
            otherwise render it directly so a plain component test stays simple. */}
        {path ? <Routes><Route path={path} element={children} /></Routes> : children}
      </MemoryRouter>
    );
  }

  return { user, ...render(ui, { wrapper: Wrapper, ...options }) };
}

/** Resolves on the next macrotask - lets a pending promise chain settle without a fixed sleep. */
export const flushPromises = () => new Promise((resolve) => setTimeout(resolve, 0));
