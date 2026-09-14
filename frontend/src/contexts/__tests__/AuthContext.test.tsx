import { StrictMode, useState } from 'react';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthProvider, useAuth } from '../AuthContext';
import { ApiError, NetworkError, api } from '../../lib/api';
import type { AuthResultDto } from '../../lib/types';

/**
 * Refresh-token handling, driven through the real provider.
 *
 * The bug this file exists for: refresh tokens are one-time-use (redeeming one
 * revokes it and issues a replacement), so two refreshes started from the SAME
 * stored token cannot both succeed. React StrictMode double-invokes the mount
 * effect in development, so both invocations read the same stored token before
 * either resolves - one gets a 200, the other a 401 "already used", and the
 * 401's catch signs the organizer out. It presented as being randomly logged
 * out on reload. The same shape appears in any build whenever two screens hit
 * a 401 at the same moment.
 *
 * These tests deliberately do NOT use `authContextMock`: what is under test is
 * the provider that stand-in replaces.
 */

const REFRESH_TOKEN_KEY = 'bookingtracker.refreshToken';

function authResult(overrides: Partial<AuthResultDto> = {}): AuthResultDto {
  return {
    organizerId: '99999999-9999-9999-9999-999999999999',
    name: 'Demo Organizer',
    email: 'organizer@example.com',
    accessToken: 'access-token',
    accessTokenExpiresAt: '2099-01-01T00:00:00Z',
    refreshToken: 'rotated-refresh-token',
    refreshTokenExpiresAt: '2099-01-01T00:00:00Z',
    ...overrides,
  };
}

/** A promise plus the handles to settle it, so a test can hold a refresh open. */
function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

/**
 * Surfaces the pieces of the context a test needs to assert on, and exposes
 * `callProtected`/`logout` as buttons so they can be driven the way a screen
 * drives them.
 */
function AuthProbe({ onCall }: { onCall?: (token: string) => Promise<unknown> } = {}) {
  const { organizer, isInitializing, callProtected, login, logout, getAccessToken } = useAuth();
  const [callError, setCallError] = useState<string | null>(null);

  return (
    <div>
      <span data-testid="state">
        {isInitializing ? 'initializing' : organizer ? `signed-in:${organizer.email}` : 'signed-out'}
      </span>
      <span data-testid="access-token">{getAccessToken() ?? 'none'}</span>
      <span data-testid="call-error">{callError ?? ''}</span>
      <button
        onClick={() => {
          setCallError(null);
          callProtected((token) => (onCall ? onCall(token) : Promise.resolve(token))).catch((e) =>
            setCallError(e instanceof ApiError ? `ApiError:${e.status}` : String(e)),
          );
        }}
      >
        protected call
      </button>
      <button onClick={() => void login('organizer@example.com', 'Passw0rd!').catch(() => undefined)}>
        sign in
      </button>
      <button onClick={() => void logout()}>sign out</button>
    </div>
  );
}

/** Mounts under StrictMode, which is what double-invokes the mount effect. */
function renderStrict(ui: React.ReactNode) {
  return render(
    <StrictMode>
      <AuthProvider>{ui}</AuthProvider>
    </StrictMode>,
  );
}

beforeEach(() => {
  vi.spyOn(api.auth, 'logout').mockResolvedValue(undefined);
});

describe('restoring a session on load', () => {
  it('issues only one refresh request under StrictMode double-invocation', async () => {
    // REGRESSION: the mount effect ran twice, both reads took the same stored
    // token, and the loser's 401 cleared the session.
    localStorage.setItem(REFRESH_TOKEN_KEY, 'stored-refresh-token');
    const refresh = vi.spyOn(api.auth, 'refresh').mockResolvedValue(authResult());

    renderStrict(<AuthProbe />);

    await waitFor(() =>
      expect(screen.getByTestId('state')).toHaveTextContent('signed-in:organizer@example.com'),
    );
    expect(refresh).toHaveBeenCalledTimes(1);
  });

  it('stays signed in when the second invocation would have been rejected as already-used', async () => {
    // The precise production sequence: first redemption succeeds, a second
    // redemption of the same token is refused. Only one request may be made,
    // so the rejection must never happen at all.
    localStorage.setItem(REFRESH_TOKEN_KEY, 'stored-refresh-token');
    vi.spyOn(api.auth, 'refresh').mockImplementation((token) =>
      token === 'stored-refresh-token'
        ? Promise.resolve(authResult())
        : Promise.reject(new ApiError(401, { title: 'Refresh token is expired or has already been used.' })),
    );

    renderStrict(<AuthProbe />);

    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('signed-in'));
    // And the rotated token replaced the one that was spent.
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBe('rotated-refresh-token');
  });

  it('does not attempt a refresh at all with no stored token', async () => {
    const refresh = vi.spyOn(api.auth, 'refresh');

    renderStrict(<AuthProbe />);

    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('signed-out'));
    expect(refresh).not.toHaveBeenCalled();
  });

  it('signs out and clears the stored token when the refresh is genuinely rejected', async () => {
    localStorage.setItem(REFRESH_TOKEN_KEY, 'expired-token');
    vi.spyOn(api.auth, 'refresh').mockRejectedValue(new ApiError(401, { title: 'Expired.' }));

    renderStrict(<AuthProbe />);

    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('signed-out'));
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBeNull();
  });

  it('finishes initializing even when the server is unreachable', async () => {
    localStorage.setItem(REFRESH_TOKEN_KEY, 'stored-refresh-token');
    vi.spyOn(api.auth, 'refresh').mockRejectedValue(new NetworkError('down'));

    renderStrict(<AuthProbe />);

    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('signed-out'));
  });
});

describe('concurrent callers share one refresh', () => {
  it('refreshes once for two protected calls that both hit 401', async () => {
    // Without sharing, both would redeem the same one-time-use token and the
    // loser's 401 would sign the organizer out mid-session.
    localStorage.setItem(REFRESH_TOKEN_KEY, 'stored-refresh-token');
    const gate = deferred<AuthResultDto>();
    const refresh = vi.spyOn(api.auth, 'refresh').mockReturnValue(gate.promise);

    let callCount = 0;
    const onCall = (token: string) => {
      callCount += 1;
      // Every call fails with 401 until the refreshed token arrives.
      return token === 'access-token-2'
        ? Promise.resolve('ok')
        : Promise.reject(new ApiError(401, { title: 'Expired.' }));
    };

    const user = userEvent.setup();
    renderStrict(<AuthProbe onCall={onCall} />);
    await waitFor(() => expect(refresh).toHaveBeenCalledTimes(1));

    // Let the mount refresh land so there is a session to make calls on.
    gate.resolve(authResult({ accessToken: 'access-token-1' }));
    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('signed-in'));

    const secondGate = deferred<AuthResultDto>();
    refresh.mockReturnValue(secondGate.promise);

    const button = screen.getByRole('button', { name: 'protected call' });
    await user.click(button);
    await user.click(button);
    await waitFor(() => expect(callCount).toBe(2));

    // Both 401s are now waiting on a refresh - and there must be exactly one.
    expect(refresh).toHaveBeenCalledTimes(2); // 1 on mount + 1 shared by both callers

    secondGate.resolve(authResult({ accessToken: 'access-token-2' }));
    await waitFor(() => expect(screen.getByTestId('access-token')).toHaveTextContent('access-token-2'));
    expect(screen.getByTestId('call-error')).toHaveTextContent('');
  });
});

describe('the guard does not wedge', () => {
  it('allows a later refresh after an earlier one failed', async () => {
    // The in-flight slot must be released however the request settles, or one
    // failure would block every refresh for the life of the tab.
    localStorage.setItem(REFRESH_TOKEN_KEY, 'stored-refresh-token');
    const refresh = vi
      .spyOn(api.auth, 'refresh')
      .mockRejectedValueOnce(new NetworkError('down'))
      .mockResolvedValue(authResult({ accessToken: 'recovered-token' }));

    const user = userEvent.setup();
    // Succeeds only once the recovered token arrives, so the retry completes
    // and the session survives - otherwise the retry's own 401 would clear it
    // and hide whether the refresh had been allowed at all.
    const onCall = (token: string) =>
      token === 'recovered-token'
        ? Promise.resolve('ok')
        : Promise.reject(new ApiError(401, { title: 'Expired.' }));
    renderStrict(<AuthProbe onCall={onCall} />);

    // The mount refresh fails, so the session is cleared.
    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('signed-out'));
    expect(refresh).toHaveBeenCalledTimes(1);

    // A fresh sign-in, then a protected call whose 401 needs a refresh: the
    // guard must let it through rather than still holding the failed attempt.
    vi.spyOn(api.auth, 'login').mockResolvedValue(
      authResult({ accessToken: 'login-token', refreshToken: 'a-new-token' }),
    );
    await user.click(screen.getByRole('button', { name: 'sign in' }));
    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('signed-in'));

    await user.click(screen.getByRole('button', { name: 'protected call' }));

    await waitFor(() => expect(refresh).toHaveBeenCalledTimes(2));
    expect(refresh).toHaveBeenLastCalledWith('a-new-token');
    // And the refresh it was finally allowed to make actually took effect.
    await waitFor(() => expect(screen.getByTestId('access-token')).toHaveTextContent('recovered-token'));
  });

  it('releases the slot after a successful refresh so the next one is a real request', async () => {
    localStorage.setItem(REFRESH_TOKEN_KEY, 'stored-refresh-token');
    const refresh = vi.spyOn(api.auth, 'refresh').mockResolvedValue(authResult({ accessToken: 'token-a' }));

    const user = userEvent.setup();
    renderStrict(<AuthProbe onCall={() => Promise.reject(new ApiError(401, { title: 'Expired.' }))} />);
    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('signed-in'));
    expect(refresh).toHaveBeenCalledTimes(1);

    await user.click(screen.getByRole('button', { name: 'protected call' }));
    await waitFor(() => expect(refresh).toHaveBeenCalledTimes(2));

    // A cached promise would have been reused instead of a second request.
    expect(refresh).toHaveBeenLastCalledWith('rotated-refresh-token');
  });
});

describe('signing out beats an in-flight refresh', () => {
  it('stays signed out when a refresh resolves after the sign-out', async () => {
    // Without a generation guard the resolving refresh would call
    // applyAuthResult and write its tokens back, putting the organizer back in
    // a session they had just ended.
    localStorage.setItem(REFRESH_TOKEN_KEY, 'stored-refresh-token');
    const gate = deferred<AuthResultDto>();
    vi.spyOn(api.auth, 'refresh').mockReturnValue(gate.promise);

    const user = userEvent.setup();
    renderStrict(<AuthProbe />);
    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('initializing'));

    await user.click(screen.getByRole('button', { name: 'sign out' }));
    gate.resolve(authResult());

    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('signed-out'));
    expect(screen.getByTestId('access-token')).toHaveTextContent('none');
    // The refresh token it returned must not have been persisted.
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBeNull();
  });

  it('leaves no access token behind for SignalR to pick up', async () => {
    localStorage.setItem(REFRESH_TOKEN_KEY, 'stored-refresh-token');
    const gate = deferred<AuthResultDto>();
    vi.spyOn(api.auth, 'refresh').mockReturnValue(gate.promise);

    const user = userEvent.setup();
    renderStrict(<AuthProbe />);

    await user.click(screen.getByRole('button', { name: 'sign out' }));
    gate.resolve(authResult({ accessToken: 'should-never-be-used' }));
    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('signed-out'));

    expect(screen.getByTestId('access-token')).not.toHaveTextContent('should-never-be-used');
  });
});

describe('callProtected', () => {
  it('refuses to call anything when there is no access token', async () => {
    const user = userEvent.setup();
    renderStrict(<AuthProbe />);
    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('signed-out'));

    await user.click(screen.getByRole('button', { name: 'protected call' }));

    await waitFor(() => expect(screen.getByTestId('call-error')).toHaveTextContent('ApiError:401'));
  });

  it('signs out and rethrows the original 401 when the refresh fails', async () => {
    localStorage.setItem(REFRESH_TOKEN_KEY, 'stored-refresh-token');
    vi.spyOn(api.auth, 'refresh')
      .mockResolvedValueOnce(authResult())
      .mockRejectedValue(new ApiError(401, { title: 'Refresh token is expired or has already been used.' }));

    const user = userEvent.setup();
    renderStrict(<AuthProbe onCall={() => Promise.reject(new ApiError(401, { title: 'Expired.' }))} />);
    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('signed-in'));

    await user.click(screen.getByRole('button', { name: 'protected call' }));

    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('signed-out'));
    expect(screen.getByTestId('call-error')).toHaveTextContent('ApiError:401');
  });

  it('passes a non-401 failure straight through without refreshing', async () => {
    localStorage.setItem(REFRESH_TOKEN_KEY, 'stored-refresh-token');
    const refresh = vi.spyOn(api.auth, 'refresh').mockResolvedValue(authResult());

    const user = userEvent.setup();
    renderStrict(<AuthProbe onCall={() => Promise.reject(new ApiError(500, { title: 'Boom.' }))} />);
    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('signed-in'));

    await user.click(screen.getByRole('button', { name: 'protected call' }));

    await waitFor(() => expect(screen.getByTestId('call-error')).toHaveTextContent('ApiError:500'));
    expect(refresh).toHaveBeenCalledTimes(1); // the mount one only
    expect(screen.getByTestId('state')).toHaveTextContent('signed-in');
  });

  it('retries the call once with the refreshed token and returns its result', async () => {
    localStorage.setItem(REFRESH_TOKEN_KEY, 'stored-refresh-token');
    vi.spyOn(api.auth, 'refresh')
      .mockResolvedValueOnce(authResult({ accessToken: 'stale-token' }))
      .mockResolvedValue(authResult({ accessToken: 'fresh-token' }));

    const seen: string[] = [];
    const onCall = (token: string) => {
      seen.push(token);
      return token === 'fresh-token'
        ? Promise.resolve('ok')
        : Promise.reject(new ApiError(401, { title: 'Expired.' }));
    };

    const user = userEvent.setup();
    renderStrict(<AuthProbe onCall={onCall} />);
    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('signed-in'));

    await user.click(screen.getByRole('button', { name: 'protected call' }));

    await waitFor(() => expect(seen).toEqual(['stale-token', 'fresh-token']));
    expect(screen.getByTestId('call-error')).toHaveTextContent('');
  });
});

describe('logout', () => {
  it('revokes the stored token and clears the session', async () => {
    localStorage.setItem(REFRESH_TOKEN_KEY, 'stored-refresh-token');
    vi.spyOn(api.auth, 'refresh').mockResolvedValue(authResult());
    const logoutCall = vi.spyOn(api.auth, 'logout').mockResolvedValue(undefined);

    const user = userEvent.setup();
    renderStrict(<AuthProbe />);
    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('signed-in'));

    await user.click(screen.getByRole('button', { name: 'sign out' }));

    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('signed-out'));
    expect(logoutCall).toHaveBeenCalledWith('rotated-refresh-token');
    expect(localStorage.getItem(REFRESH_TOKEN_KEY)).toBeNull();
  });

  it('still signs out locally when the revoke call fails', async () => {
    localStorage.setItem(REFRESH_TOKEN_KEY, 'stored-refresh-token');
    vi.spyOn(api.auth, 'refresh').mockResolvedValue(authResult());
    vi.spyOn(api.auth, 'logout').mockRejectedValue(new NetworkError('down'));

    const user = userEvent.setup();
    renderStrict(<AuthProbe />);
    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('signed-in'));

    await user.click(screen.getByRole('button', { name: 'sign out' }));

    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('signed-out'));
  });
});

describe('useAuth outside a provider', () => {
  it('fails loudly rather than handing back a null context', () => {
    function Bare() {
      useAuth();
      return null;
    }
    // React logs the thrown error; the assertion is that it throws at all.
    const spy = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    expect(() => render(<Bare />)).toThrow(/must be used within an AuthProvider/);
    spy.mockRestore();
  });
});
