import { screen, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { HomePage } from '../HomePage';
import { renderWithProviders } from '../../test/render';
import { authState, resetAuthState } from '../../test/authContextMock';

vi.mock('../../contexts/AuthContext', () => import('../../test/authContextMock'));

/**
 * The front door, which used to be three links in a card and never said what
 * the product was.
 *
 * Asserted on what a visitor can act on rather than on copy: the one primary
 * action, the way in for someone who already has an account, and the demo -
 * which is the only thing the old page offered and had to survive.
 */
describe('HomePage', () => {
  beforeEach(() => {
    resetAuthState();
  });

  describe('signed out', () => {
    beforeEach(() => {
      authState.organizer = null;
    });

    it('says what the product does', async () => {
      renderWithProviders(<HomePage />);

      expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent(/book your time/i);
      expect(screen.getByText(/organizer dashboard that records every step/i)).toBeInTheDocument();
    });

    it('says who it is for', async () => {
      renderWithProviders(<HomePage />);

      expect(screen.getByText(/consultants, tutors, advisors/i)).toBeInTheDocument();
    });

    it('leads with creating an account', async () => {
      renderWithProviders(<HomePage />);

      expect(screen.getByRole('link', { name: /create an organizer account/i })).toHaveAttribute(
        'href',
        '/register',
      );
    });

    it('lets someone who already has an account sign in', async () => {
      renderWithProviders(<HomePage />);

      const account = screen.getByRole('navigation', { name: 'Account' });
      expect(within(account).getByRole('link', { name: /sign in/i })).toHaveAttribute('href', '/login');
      expect(within(account).getByRole('link', { name: /create account/i })).toHaveAttribute('href', '/register');
    });

    it('does not offer the dashboard to someone who cannot reach it', async () => {
      renderWithProviders(<HomePage />);

      expect(screen.queryByRole('link', { name: /go to dashboard/i })).not.toBeInTheDocument();
    });

    it('keeps the demo booking page reachable', async () => {
      renderWithProviders(<HomePage />);

      expect(screen.getByRole('link', { name: /demo booking page/i })).toHaveAttribute(
        'href',
        '/book/demo-30-min-meeting',
      );
    });

    it('states the hours a new account starts on, matching what creating a page actually does', async () => {
      renderWithProviders(<HomePage />);

      expect(screen.getByText(/monday to friday, 09:00–17:00/i)).toBeInTheDocument();
    });
  });

  describe('signed in', () => {
    it('offers the dashboard instead of an account it already has', async () => {
      renderWithProviders(<HomePage />);

      expect(screen.getByRole('link', { name: /go to dashboard/i })).toHaveAttribute('href', '/dashboard');
      expect(screen.queryByRole('link', { name: /create an organizer account/i })).not.toBeInTheDocument();
      expect(screen.queryByRole('navigation', { name: 'Account' })).not.toBeInTheDocument();
    });

    it('still explains the product rather than redirecting away from it', async () => {
      // A landing page you cannot read once signed in is a redirect wearing a
      // page's clothes.
      renderWithProviders(<HomePage />);

      expect(screen.getByRole('heading', { level: 1 })).toBeInTheDocument();
    });
  });
});
