import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { api, ApiError, errorMessage } from '../lib/api';
import type { AuthResultDto } from '../lib/types';

const REFRESH_TOKEN_STORAGE_KEY = 'bookingtracker.refreshToken';

/**
 * Login/register are organizer-facing (in development, the person running the
 * app), so naming the API is a useful hint here - unlike the public booking
 * pages, where the default connectivity wording in lib/api.ts is right.
 */
const AUTH_NETWORK_MESSAGE = 'Could not reach the server. Check that the API is running.';

interface OrganizerProfile {
  id: string;
  name: string;
  email: string;
}

interface AuthContextValue {
  organizer: OrganizerProfile | null;
  isAuthenticated: boolean;
  isInitializing: boolean;
  authError: string | null;
  login: (email: string, password: string) => Promise<void>;
  register: (name: string, email: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  /**
   * Runs an organizer-only API call with the current access token. If the
   * call fails with 401 (access token expired), silently refreshes once and
   * retries - callers never have to think about token expiry.
   */
  callProtected: <T>(fn: (accessToken: string) => Promise<T>) => Promise<T>;
  /**
   * Current access token, if any, for callers that can't go through
   * callProtected (e.g. SignalR's accessTokenFactory, which needs a
   * synchronous read rather than a wrapped request/retry-on-401 call).
   */
  getAccessToken: () => string | null;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [organizer, setOrganizer] = useState<OrganizerProfile | null>(null);
  const [isInitializing, setIsInitializing] = useState(true);
  const [authError, setAuthError] = useState<string | null>(null);
  const accessTokenRef = useRef<string | null>(null);

  // Refresh tokens are one-time-use: redeeming one revokes it and issues a
  // replacement. So two refreshes started
  // from the SAME stored token cannot both succeed - the second is correctly
  // rejected as already-used, and its caller reads that 401 as "the session is
  // gone" and signs the organizer out.
  //
  // That is not hypothetical. React StrictMode double-invokes the mount effect
  // below in development, and two components can independently hit a 401 and
  // ask for a refresh at the same moment in any build. Both are the same
  // problem: more than one refresh in flight for one token.
  //
  // refreshInFlightRef is the same lever useBookingSessionTracker.flush() uses
  // for its own duplicate-request problem - concurrent callers join the request
  // already in the air instead of starting a second one. The ref survives
  // StrictMode's simulated remount, which is what makes it work there.
  const refreshInFlightRef = useRef<Promise<AuthResultDto> | null>(null);

  // Bumped every time the session is torn down, so a refresh that resolves
  // AFTER a sign-out cannot write its tokens back and quietly resurrect the
  // session it belongs to.
  const sessionGenerationRef = useRef(0);

  const applyAuthResult = useCallback((result: AuthResultDto) => {
    accessTokenRef.current = result.accessToken;
    setOrganizer({ id: result.organizerId, name: result.name, email: result.email });
    localStorage.setItem(REFRESH_TOKEN_STORAGE_KEY, result.refreshToken);
  }, []);

  const clearAuth = useCallback(() => {
    sessionGenerationRef.current += 1;
    accessTokenRef.current = null;
    setOrganizer(null);
    localStorage.removeItem(REFRESH_TOKEN_STORAGE_KEY);
  }, []);

  /**
   * Trades the stored refresh token for a fresh access token, coalescing
   * concurrent callers onto one request. The single refresh path in this
   * provider - both the reload-restore effect and callProtected's 401 retry go
   * through it, so there is never a second way to spend a refresh token.
   *
   * Rejects (rather than signing out) so each caller decides what a failure
   * means: the mount effect clears a session that could not be restored, while
   * callProtected rethrows the original 401 it was retrying.
   */
  const refreshSession = useCallback((): Promise<AuthResultDto> => {
    const alreadyRunning = refreshInFlightRef.current;
    if (alreadyRunning) return alreadyRunning;

    const storedRefreshToken = localStorage.getItem(REFRESH_TOKEN_STORAGE_KEY);
    if (!storedRefreshToken) {
      return Promise.reject(new ApiError(401, { title: 'Not signed in.' }));
    }

    const generation = sessionGenerationRef.current;
    const attempt = api.auth.refresh(storedRefreshToken).then((result) => {
      if (sessionGenerationRef.current !== generation) {
        // Signed out while this was in flight. The tokens are valid but belong
        // to a session the organizer has already ended, so they are dropped
        // rather than stored - otherwise signing out during a refresh would
        // leave them signed back in a moment later.
        throw new ApiError(401, { title: 'Signed out.' });
      }
      applyAuthResult(result);
      return result;
    });

    refreshInFlightRef.current = attempt;
    // Release the slot however it settles, so a failed refresh never wedges the
    // guard shut against a later legitimate one. The catch is only here to keep
    // this bookkeeping chain from surfacing as an unhandled rejection; the
    // rejection itself still reaches whoever called refreshSession.
    void attempt
      .catch(() => undefined)
      .then(() => {
        if (refreshInFlightRef.current === attempt) refreshInFlightRef.current = null;
      });

    return attempt;
  }, [applyAuthResult]);

  // On load, trade a persisted refresh token for a fresh access token so a
  // page reload doesn't force the organizer to log in again.
  useEffect(() => {
    if (!localStorage.getItem(REFRESH_TOKEN_STORAGE_KEY)) {
      setIsInitializing(false);
      return;
    }

    refreshSession()
      .catch(() => clearAuth())
      .finally(() => setIsInitializing(false));
  }, [refreshSession, clearAuth]);

  const login = useCallback(
    async (email: string, password: string) => {
      setAuthError(null);
      try {
        applyAuthResult(await api.auth.login(email, password));
      } catch (e) {
        setAuthError(errorMessage(e, 'Failed to sign in.', AUTH_NETWORK_MESSAGE));
        throw e;
      }
    },
    [applyAuthResult],
  );

  const register = useCallback(
    async (name: string, email: string, password: string) => {
      setAuthError(null);
      try {
        applyAuthResult(await api.auth.register(name, email, password));
      } catch (e) {
        setAuthError(errorMessage(e, 'Failed to register.', AUTH_NETWORK_MESSAGE));
        throw e;
      }
    },
    [applyAuthResult],
  );

  const logout = useCallback(async () => {
    const storedRefreshToken = localStorage.getItem(REFRESH_TOKEN_STORAGE_KEY);
    clearAuth();
    if (storedRefreshToken) {
      await api.auth.logout(storedRefreshToken).catch(() => {});
    }
  }, [clearAuth]);

  const callProtected = useCallback(
    async <T,>(fn: (accessToken: string) => Promise<T>): Promise<T> => {
      if (!accessTokenRef.current) throw new ApiError(401, { title: 'Not signed in.' });

      try {
        return await fn(accessTokenRef.current);
      } catch (e) {
        if (!(e instanceof ApiError) || e.status !== 401) throw e;

        try {
          // Shared: several screens hitting 401 at once refresh once between
          // them, rather than racing to spend the same one-time-use token and
          // signing the organizer out on whichever request loses. Only the
          // mechanism changed here - the surrounding retry-then-sign-out
          // behaviour is exactly as it was.
          const result = await refreshSession();
          return await fn(result.accessToken);
        } catch {
          clearAuth();
          throw e;
        }
      }
    },
    [refreshSession, clearAuth],
  );

  const getAccessToken = useCallback(() => accessTokenRef.current, []);

  const value = useMemo<AuthContextValue>(
    () => ({
      organizer,
      isAuthenticated: organizer !== null,
      isInitializing,
      authError,
      login,
      register,
      logout,
      callProtected,
      getAccessToken,
    }),
    [organizer, isInitializing, authError, login, register, logout, callProtected, getAccessToken],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within an AuthProvider.');
  return ctx;
}
