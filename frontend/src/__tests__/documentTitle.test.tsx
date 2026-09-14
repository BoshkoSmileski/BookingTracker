import { screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { HomePage } from '../pages/HomePage';
import { LoginPage } from '../pages/LoginPage';
import { RegisterPage } from '../pages/RegisterPage';
import { ManageBookingPage } from '../pages/ManageBookingPage';
import { CancelBookingPage } from '../pages/CancelBookingPage';
import { RescheduleBookingPage } from '../pages/RescheduleBookingPage';
import { NotFoundPage } from '../pages/NotFoundPage';
import { api } from '../lib/api';
import { publicBooking } from '../test/factories';
import { renderWithProviders } from '../test/render';

vi.mock('../contexts/AuthContext', () => import('../test/authContextMock'));

/**
 * The browser tab, per route.
 *
 * Every screen used to render the single `<title>` in index.html, so a dozen
 * open tabs were indistinguishable, history was unsearchable, and a bookmark
 * named itself after the product rather than the page.
 *
 * Public and auth routes are covered here because they mount without the
 * dashboard shell; the organizer screens each set theirs the same way, through
 * the one `useDocumentTitle` hook, and their own page tests already mount them.
 */
describe('document titles', () => {
  beforeEach(() => {
    document.title = 'unset';
  });

  it('names the product alone on the landing page', () => {
    renderWithProviders(<HomePage />);
    expect(document.title).toBe('BookingTracker');
  });

  it('names the sign-in and register screens', () => {
    const { unmount } = renderWithProviders(<LoginPage />);
    expect(document.title).toBe('Sign in · BookingTracker');
    unmount();

    renderWithProviders(<RegisterPage />);
    expect(document.title).toBe('Create account · BookingTracker');
  });

  it('names the three guest management screens', async () => {
    vi.spyOn(api.bookings, 'getByToken').mockResolvedValue(publicBooking());

    const manage = renderWithProviders(<ManageBookingPage />, {
      route: '/manage/tok', path: '/manage/:token',
    });
    expect(document.title).toBe('Manage booking · BookingTracker');
    manage.unmount();

    const cancel = renderWithProviders(<CancelBookingPage />, {
      route: '/manage/tok/cancel', path: '/manage/:token/cancel',
    });
    expect(document.title).toBe('Cancel booking · BookingTracker');
    cancel.unmount();

    renderWithProviders(<RescheduleBookingPage />, {
      route: '/manage/tok/reschedule', path: '/manage/:token/reschedule',
    });
    expect(document.title).toBe('Reschedule booking · BookingTracker');
  });

  it('names the 404 screen', () => {
    renderWithProviders(<NotFoundPage />);
    expect(document.title).toBe('Page not found · BookingTracker');
  });

  it('waits for the data a public booking page names itself after', async () => {
    // The rule that keeps a tab from flashing a placeholder: no title until the
    // page it describes has actually loaded.
    const { BookingPage } = await import('../pages/BookingPage');
    let resolvePage: (p: unknown) => void = () => {};
    vi.spyOn(api, 'getBookingPage').mockReturnValue(
      new Promise((r) => { resolvePage = r as (p: unknown) => void; }),
    );
    vi.spyOn(api, 'startSession').mockResolvedValue(
      // Never reached before the assertion below; the page gate is the slow one.
      (await import('../test/factories')).bookingSession({ status: 'Active' }),
    );

    renderWithProviders(<BookingPage />, { route: '/book/demo', path: '/book/:slug' });
    expect(document.title).toBe('BookingTracker');

    const { bookingPage } = await import('../test/factories');
    resolvePage(bookingPage({ title: '30 Minute Meeting', organizerName: 'Demo Organizer' }));

    await waitFor(() =>
      expect(document.title).toBe('30 Minute Meeting — Demo Organizer · BookingTracker'),
    );
    // And the page really did render, so this is not passing on a stuck screen.
    expect(await screen.findByRole('heading', { name: '30 Minute Meeting' })).toBeInTheDocument();
  });
});
