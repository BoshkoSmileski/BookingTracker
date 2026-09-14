/* oxlint-disable react/only-export-components -- This module deliberately mixes a
   component (AuthProvider) with hooks and state helpers because it stands in for
   contexts/AuthContext, whose shape it must mirror exactly. The rule guards React
   Fast Refresh, which never applies to a module that only ever loads under test. */
import { vi } from 'vitest';
import type { ReactNode } from 'react';

/**
 * Stand-in for contexts/AuthContext in page tests.
 *
 * Page tests are about the page, not about token refresh: the real provider
 * would need a refresh-token round trip before rendering anything. This keeps
 * `callProtected`'s contract (hand the callback an access token, return its
 * promise) so pages exercise their real data-loading code path, while the
 * auth machinery itself stays covered by its own tests.
 *
 * Use it as:
 *   vi.mock('../../contexts/AuthContext', () => import('../../test/authContextMock'));
 * A vi.mock factory is hoisted above imports, so it must pull the module in
 * itself rather than closing over one.
 */

export const TEST_ACCESS_TOKEN = 'test-access-token';

/** Mutable so a test can flip to a signed-out organizer before rendering. */
export const authState = {
  organizer: { id: '99999999-9999-9999-9999-999999999999', name: 'Demo Organizer', email: 'organizer@example.com' } as
    | { id: string; name: string; email: string }
    | null,
  isInitializing: false,
  authError: null as string | null,
};

export function resetAuthState() {
  authState.organizer = {
    id: '99999999-9999-9999-9999-999999999999',
    name: 'Demo Organizer',
    email: 'organizer@example.com',
  };
  authState.isInitializing = false;
  authState.authError = null;
  login.mockClear();
  register.mockClear();
  logout.mockClear();
}

/**
 * `vi.fn()` rather than plain async no-ops, and module-level like
 * `callProtected` below for the same referential-stability reason.
 *
 * Spying on these through the module namespace (`vi.spyOn(authMock, 'login')`)
 * cannot work: `useAuth` closes over the local binding, so the namespace
 * property and the value the hook hands out are two different things under ESM.
 * Being mock functions from the start means a test can assert what the form
 * submitted - which is the only interesting thing about a sign-in form - and
 * `resetAuthState` clears them between tests.
 */
export const logout = vi.fn(async () => {});
export const login = vi.fn(async (_email: string, _password: string) => {});
export const register = vi.fn(async (_name: string, _email: string, _password: string) => {});

/**
 * Module-level, so every render hands back the SAME function reference.
 *
 * This matters more than it looks: the real AuthContext memoizes callProtected
 * with useCallback, and pages put it in useEffect dependency arrays. A fake that
 * returned a fresh closure each render would re-run those effects on every
 * render - re-fetching and clobbering local state - so the component would
 * behave differently under test than in production. Preserving referential
 * stability is part of the contract being faked, not an optimization.
 */
const callProtected = <T,>(fn: (accessToken: string) => Promise<T>): Promise<T> => fn(TEST_ACCESS_TOKEN);
const getAccessToken = () => TEST_ACCESS_TOKEN;

export function useAuth() {
  return {
    organizer: authState.organizer,
    isAuthenticated: authState.organizer !== null,
    isInitializing: authState.isInitializing,
    authError: authState.authError,
    login,
    register,
    logout,
    // Real signature: run the caller's function with a token and return its promise.
    // Errors propagate exactly as they do in production, so pages' catch blocks run.
    callProtected,
    getAccessToken,
  };
}

export function AuthProvider({ children }: { children: ReactNode }) {
  return <>{children}</>;
}
