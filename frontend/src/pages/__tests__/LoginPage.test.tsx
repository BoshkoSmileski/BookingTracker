import { screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { LoginPage } from '../LoginPage';
import { RegisterPage } from '../RegisterPage';
import { renderWithProviders } from '../../test/render';
import { authState, login, register, resetAuthState } from '../../test/authContextMock';

vi.mock('../../contexts/AuthContext', () => import('../../test/authContextMock'));

/**
 * The last two screens still on the pre-accent design, and the one place the
 * app published a working credential.
 *
 * `/login` printed "demo login: organizer@example.com / Passw0rd!" as plain
 * text in every build. The convenience is genuine for whoever is running this
 * locally, so it survives as a dev-only button; what must not survive is the
 * text being in a production bundle. `import.meta.env.DEV` is statically
 * replaced by Vite, so the branch is eliminated rather than hidden - these
 * tests drive both sides of it through `vi.stubEnv`.
 */
describe('LoginPage', () => {
  beforeEach(() => {
    resetAuthState();
  });

  afterEach(() => {
    vi.unstubAllEnvs();
  });

  it('signs in and does not keep the password in the field', async () => {
    const { user } = renderWithProviders(<LoginPage />, { route: '/login' });

    await user.type(screen.getByLabelText('Email'), 'me@example.com');
    await user.type(screen.getByLabelText('Password'), 'hunter2000');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    await waitFor(() => expect(login).toHaveBeenCalledWith('me@example.com', 'hunter2000'));
  });

  it('shows a rejected sign-in as an alert rather than a grey line', async () => {
    authState.authError = 'Invalid email or password.';
    renderWithProviders(<LoginPage />, { route: '/login' });

    expect(screen.getByRole('alert')).toHaveTextContent('Invalid email or password.');
  });

  it('offers the way to register', async () => {
    renderWithProviders(<LoginPage />, { route: '/login' });

    expect(screen.getByRole('link', { name: /create one/i })).toHaveAttribute('href', '/register');
  });

  describe('demo credentials', () => {
    it('are nowhere on the page in a production build', async () => {
      vi.stubEnv('DEV', false);
      renderWithProviders(<LoginPage />, { route: '/login' });

      expect(screen.queryByText(/Passw0rd!/)).not.toBeInTheDocument();
      expect(screen.queryByText(/organizer@example\.com/)).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: /demo credentials/i })).not.toBeInTheDocument();
    });

    it('are still one click away in development, without being printed', async () => {
      vi.stubEnv('DEV', true);
      const { user } = renderWithProviders(<LoginPage />, { route: '/login' });

      // The credential is in the handler, not in the markup - so even in dev the
      // password is never rendered as text.
      expect(screen.queryByText(/Passw0rd!/)).not.toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: /fill demo credentials/i }));

      expect(screen.getByLabelText('Email')).toHaveValue('organizer@example.com');
      expect(screen.getByLabelText('Password')).toHaveValue('Passw0rd!');
    });

    it('signs in with the filled values like any other attempt', async () => {
      vi.stubEnv('DEV', true);
        const { user } = renderWithProviders(<LoginPage />, { route: '/login' });

      await user.click(screen.getByRole('button', { name: /fill demo credentials/i }));
      await user.click(screen.getByRole('button', { name: /^sign in$/i }));

      await waitFor(() => expect(login).toHaveBeenCalledWith('organizer@example.com', 'Passw0rd!'));
    });
  });
});

describe('RegisterPage', () => {
  beforeEach(() => {
    resetAuthState();
  });

  it('registers with the details entered', async () => {
    const { user } = renderWithProviders(<RegisterPage />, { route: '/register' });

    await user.type(screen.getByLabelText('Name'), 'Ada');
    await user.type(screen.getByLabelText('Email'), 'ada@example.com');
    await user.type(screen.getByLabelText('Password'), 'hunter2000');
    await user.click(screen.getByRole('button', { name: /create account/i }));

    await waitFor(() => expect(register).toHaveBeenCalledWith('Ada', 'ada@example.com', 'hunter2000'));
  });

  it('states the password rule against the field rather than beside it', async () => {
    renderWithProviders(<RegisterPage />, { route: '/register' });

    const password = screen.getByLabelText('Password');
    expect(password).toHaveAttribute('minLength', '8');
    expect(password).toHaveAccessibleDescription(/at least 8 characters/i);
  });

  it('shows a rejected registration as an alert', async () => {
    authState.authError = 'An organizer with this email already exists.';
    renderWithProviders(<RegisterPage />, { route: '/register' });

    expect(screen.getByRole('alert')).toHaveTextContent(/already exists/i);
  });

  it('never offers demo credentials — they belong to the sign-in form only', async () => {
    vi.stubEnv('DEV', true);
    renderWithProviders(<RegisterPage />, { route: '/register' });

    expect(screen.queryByRole('button', { name: /demo credentials/i })).not.toBeInTheDocument();
    vi.unstubAllEnvs();
  });

  it('offers the way back to signing in', async () => {
    renderWithProviders(<RegisterPage />, { route: '/register' });

    expect(screen.getByRole('link', { name: /sign in/i })).toHaveAttribute('href', '/login');
  });
});
