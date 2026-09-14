import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import App from '../App';
import { AuthProvider } from '../contexts/AuthContext';
import { api } from '../lib/api';
import { bookingPageDetail, bookingPageSummary, notificationSettings } from '../test/factories';
import type { AuthResultDto } from '../lib/types';

/**
 * The route table and the authentication redirect, driven through the real
 * `App` and the real `AuthProvider`.
 *
 * Two things had no coverage and no implementation. There was no catch-all at
 * all, so any unmatched path rendered `null` - a blank white page with no
 * navigation, indistinguishable from a crash. And `ProtectedRoute` discarded
 * the address it refused, so every bookmarked deep link landed on /dashboard
 * after signing in.
 *
 * This is the one test file that does NOT use `authContextMock`: that stand-in
 * is deliberately not reactive, and what is being tested here is precisely the
 * transition from signed-out to signed-in. The real provider is driven through
 * the `api.auth` boundary instead, exactly as production drives it.
 *
 * MemoryRouter rather than renderWithProviders, which supplies a router of its
 * own and so cannot host a component tree that already declares one.
 */

const PAGE_ID = '11111111-1111-1111-1111-111111111111';
const REFRESH_TOKEN_KEY = 'bookingtracker.refreshToken';

function authResult(): AuthResultDto {
  return {
    organizerId: '99999999-9999-9999-9999-999999999999',
    name: 'Demo Organizer',
    email: 'organizer@example.com',
    accessToken: 'access-token',
    accessTokenExpiresAt: '2099-01-01T00:00:00Z',
    refreshToken: 'refresh-token',
    refreshTokenExpiresAt: '2099-01-01T00:00:00Z',
  };
}

/** A stored refresh token is how the real provider restores a session on load. */
function alreadySignedIn() {
  localStorage.setItem(REFRESH_TOKEN_KEY, 'stored-refresh-token');
  vi.spyOn(api.auth, 'refresh').mockResolvedValue(authResult());
}

/** `route` is a MemoryRouter entry, so a test can also plant router state. */
function renderAt(route: string | { pathname: string; state?: unknown }) {
  const user = userEvent.setup();
  return {
    user,
    ...render(
      <MemoryRouter initialEntries={[route]}>
        <AuthProvider>
          <App />
        </AuthProvider>
      </MemoryRouter>,
    ),
  };
}

async function signIn(user: ReturnType<typeof userEvent.setup>) {
  vi.spyOn(api.auth, 'login').mockResolvedValue(authResult());
  await user.type(await screen.findByLabelText('Email'), 'organizer@example.com');
  await user.type(screen.getByLabelText('Password'), 'Passw0rd!');
  await user.click(screen.getByRole('button', { name: /sign in/i }));
}

beforeEach(() => {
  vi.spyOn(api.organizer, 'getMyBookingPages').mockResolvedValue([bookingPageSummary({ id: PAGE_ID })]);
  vi.spyOn(api.organizer, 'getNotificationSettings').mockResolvedValue(notificationSettings());
  vi.spyOn(api.organizer, 'getBookingPage').mockResolvedValue(bookingPageDetail({ id: PAGE_ID }));
  vi.spyOn(api.organizer, 'getSessions').mockResolvedValue([]);
  vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(null);
  vi.spyOn(api.availability, 'getExceptions').mockResolvedValue([]);
  vi.spyOn(api.availability, 'getOverrides').mockResolvedValue([]);
});

describe('unknown routes', () => {
  it('explains a mistyped public address instead of rendering nothing', async () => {
    renderAt('/nonsense');

    expect(await screen.findByRole('heading', { name: /page not found/i })).toBeInTheDocument();
    expect(screen.getByText(/does not exist/i)).toBeInTheDocument();
  });

  it('offers a way out of a public dead end', async () => {
    renderAt('/nonsense');

    expect(await screen.findByRole('link', { name: /go to the home page/i })).toHaveAttribute('href', '/');
  });

  it('does not ask a guest who mistyped a booking link to sign in', async () => {
    // A wrong address is not a reason to demand credentials, and /book/... is
    // the one URL a guest is ever handed.
    renderAt('/book/typo/extra/segments');

    expect(await screen.findByRole('heading', { name: /page not found/i })).toBeInTheDocument();
    expect(screen.queryByLabelText('Password')).not.toBeInTheDocument();
  });

  it('keeps an organizer inside the shell for an unknown dashboard address', async () => {
    alreadySignedIn();
    renderAt('/dashboard/settings/does-not-exist');

    expect(await screen.findByRole('heading', { name: /page not found/i })).toBeInTheDocument();
    // The rail is the point: a 404 must not also cost the organizer their
    // navigation.
    expect(screen.getByRole('navigation', { name: 'Main' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /back to booking pages/i })).toHaveAttribute('href', '/dashboard');
  });

  it('still resolves every real dashboard route ahead of the catch-all', async () => {
    // The in-shell splat is scoped to /dashboard, so it must rank below the
    // routes it sits beside rather than swallowing them.
    alreadySignedIn();
    renderAt(`/dashboard/${PAGE_ID}/settings/notifications`);

    expect(await screen.findByRole('heading', { name: 'Notifications' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: /page not found/i })).not.toBeInTheDocument();
  });

  it.each([
    ['blocked-dates', 'Date exceptions'],
    ['date-overrides', 'Date exceptions'],
    ['questions', 'Booking instructions'],
  ])('resolves the legacy /%s bookmark to %s', async (legacy, heading) => {
    // REGRESSION: all three redirected with a bare `../`, which React Router
    // resolves against the route hierarchy rather than the URL. Being flat
    // routes with no parent, they every one landed on `/date-exceptions` and
    // `/instructions` - paths that do not exist. Adding a catch-all is what
    // made that visible; before it they rendered a blank page.
    alreadySignedIn();
    renderAt(`/dashboard/${PAGE_ID}/settings/${legacy}`);

    expect(await screen.findByRole('heading', { name: heading })).toBeInTheDocument();
  });
});

describe('signing in returns you to where you were going', () => {
  it('redirects a protected route to the login form', async () => {
    renderAt(`/dashboard/${PAGE_ID}/settings/notifications`);

    expect(await screen.findByRole('heading', { level: 1, name: /sign in/i })).toBeInTheDocument();
  });

  it('returns to the original route, not the default dashboard', async () => {
    const { user } = renderAt(`/dashboard/${PAGE_ID}/settings/notifications`);

    await signIn(user);

    expect(await screen.findByRole('heading', { name: 'Notifications' })).toBeInTheDocument();
  });

  it('carries the query string back with it', async () => {
    // The session list keeps its status filter in the URL, so dropping the
    // search string would return the organizer to a different list than the
    // one they had linked to.
    const { user } = renderAt(`/dashboard/${PAGE_ID}?status=Cancelled`);

    await signIn(user);

    await waitFor(() =>
      expect(api.organizer.getSessions).toHaveBeenCalledWith(expect.anything(), PAGE_ID, 'Cancelled'),
    );
  });

  it('sends an ordinary sign-in to the default destination', async () => {
    const { user } = renderAt('/login');

    await signIn(user);

    // /dashboard, which with one booking page lists it.
    expect(await screen.findByRole('heading', { name: 'Booking pages' })).toBeInTheDocument();
  });

  it('refuses an external destination planted in router state', async () => {
    // ProtectedRoute only ever stores a real Location, but the state is the one
    // input LoginPage does not control - so an open redirect must be impossible
    // rather than merely unlikely.
    const { user } = renderAt({
      pathname: '/login',
      state: { from: { pathname: 'https://evil.example/steal' } },
    });

    await signIn(user);

    expect(await screen.findByRole('heading', { name: 'Booking pages' })).toBeInTheDocument();
  });
});
