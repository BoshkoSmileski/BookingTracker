import { render, screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import userEvent from '@testing-library/user-event';
import { DashboardLayout } from '../DashboardLayout';
import { api } from '../../lib/api';
import { bookingPageSummary } from '../../test/factories';
import { resetAuthState } from '../../test/authContextMock';

vi.mock('../../contexts/AuthContext', () => import('../../test/authContextMock'));

/**
 * The layout is a route element with an <Outlet />, so it needs a router that
 * has a child route to fill it - renderWithProviders mounts a single element and
 * cannot express that.
 */
function renderAt(route: string) {
  const user = userEvent.setup();
  const result = render(
    <MemoryRouter initialEntries={[route]}>
      <Routes>
        <Route element={<DashboardLayout />}>
          <Route path="/dashboard" element={<p>workspace</p>} />
          <Route path="/dashboard/analytics" element={<p>analytics</p>} />
          <Route path="/dashboard/:pageId" element={<p>sessions</p>} />
          <Route path="/dashboard/:pageId/sessions/:sessionId" element={<p>one session</p>} />
          <Route path="/dashboard/:pageId/settings/hours" element={<p>hours</p>} />
          <Route path="/dashboard/:pageId/settings/limits" element={<p>limits</p>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
  return { user, ...result };
}

const nav = () => screen.getByRole('navigation', { name: 'Main' });

describe('DashboardLayout', () => {
  const PAGE_ID = '11111111-1111-1111-1111-111111111111';
  const OTHER_ID = '22222222-2222-2222-2222-222222222222';

  beforeEach(() => {
    resetAuthState();
    vi.spyOn(api.organizer, 'getMyBookingPages').mockResolvedValue([bookingPageSummary()]);
  });

  it('offers every top-level destination from every screen', async () => {
    // Analytics used to exist on exactly one screen, and there was no route at
    // all from inside a booking page back to the list of pages.
    renderAt(`/dashboard/${PAGE_ID}/settings/hours`);

    expect(within(nav()).getByRole('link', { name: 'Dashboard' })).toHaveAttribute('href', '/dashboard');
    expect(within(nav()).getByRole('link', { name: 'Analytics' })).toHaveAttribute('href', '/dashboard/analytics');
  });

  it('carries the organizer and Sign out on every screen, not just two of them', () => {
    renderAt(`/dashboard/${PAGE_ID}/settings/hours`);

    expect(screen.getByText('Demo Organizer')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument();
  });

  it('marks the screen you are on', () => {
    renderAt('/dashboard/analytics');

    expect(within(nav()).getByRole('link', { name: 'Analytics' })).toHaveAttribute('aria-current', 'page');
    expect(within(nav()).getByRole('link', { name: 'Dashboard' })).not.toHaveAttribute('aria-current');
  });

  it('does not mark Dashboard active merely because the URL starts with /dashboard', () => {
    // Every organizer route begins with /dashboard, so this needs an exact match
    // or the first nav item is permanently highlighted.
    renderAt(`/dashboard/${PAGE_ID}/settings/hours`);

    expect(within(nav()).getByRole('link', { name: 'Dashboard' })).not.toHaveAttribute('aria-current');
  });

  it('separates organizer-wide settings from a booking page, because they are', async () => {
    // Working hours edits one schedule shared by every booking page. Listing it
    // under a page implied the opposite.
    renderAt(`/dashboard/${PAGE_ID}/settings/hours`);
    await screen.findByRole('link', { name: '30 Minute Meeting' });

    const workspaceLinks = ['Working hours', 'Date exceptions', 'Calendar', 'Notifications'];
    for (const label of workspaceLinks) {
      expect(within(nav()).getByRole('link', { name: label })).toBeInTheDocument();
    }
    // ...and they stay reachable from a booking page, where that page's own
    // settings appear as a separate, nested group.
    expect(within(nav()).queryByRole('link', { name: 'Booking limits' })).not.toBeInTheDocument();
  });

  it('gives every workspace row an icon, so none of them starts on a different line', async () => {
    // REGRESSION: `Date overrides` shipped with no entry in the sidebar's icon
    // map, so its label began where every other row's icon did - which reads as
    // a broken indent rather than as a different kind of row.
    renderAt(`/dashboard/${PAGE_ID}/settings/hours`);
    await screen.findByRole('link', { name: '30 Minute Meeting' });

    for (const label of ['Working hours', 'Date exceptions', 'Calendar', 'Notifications']) {
      expect(within(nav()).getByRole('link', { name: label }).querySelector('svg')).not.toBeNull();
    }
  });

  it('offers the booking page settings under the page, on a page-scoped screen', async () => {
    renderAt(`/dashboard/${PAGE_ID}`);
    await within(nav()).findByRole('link', { name: '30 Minute Meeting' });

    for (const label of ['Sessions', 'Details', 'Duration & buffers', 'Booking limits', 'Instructions']) {
      expect(within(nav()).getByRole('link', { name: label })).toBeInTheDocument();
    }
  });

  it('lists the organizer booking pages, and expands only the open one', async () => {
    vi.spyOn(api.organizer, 'getMyBookingPages').mockResolvedValue([
      bookingPageSummary(),
      bookingPageSummary({ id: OTHER_ID, slug: 'discovery', title: 'Discovery Call' }),
    ]);
    renderAt(`/dashboard/${PAGE_ID}`);

    expect(await within(nav()).findByRole('link', { name: 'Discovery Call' })).toBeInTheDocument();
    // One expanded page: showing every page's settings at once would put five
    // times the pages' worth of links in the rail.
    expect(within(nav()).getAllByRole('link', { name: 'Details' })).toHaveLength(1);
    expect(within(nav()).getByRole('link', { name: 'Details' }))
      .toHaveAttribute('href', `/dashboard/${PAGE_ID}/settings/details`);
  });

  it('keeps a booking page marked while one of its own settings screens is open', async () => {
    renderAt(`/dashboard/${PAGE_ID}/settings/limits`);

    const pageLink = await within(nav()).findByRole('link', { name: '30 Minute Meeting' });
    expect(pageLink).toHaveClass('bg-accent-50');
    expect(within(nav()).getByRole('link', { name: 'Booking limits' })).toHaveAttribute('aria-current', 'page');
  });

  it('does not mark a booking page while an organizer-wide setting is open', async () => {
    // Working hours carries this page's id in its URL but edits one schedule
    // shared by every page - marking the page would claim otherwise, and the
    // URL alone cannot tell the two cases apart.
    renderAt(`/dashboard/${PAGE_ID}/settings/hours`);

    const pageLink = await within(nav()).findByRole('link', { name: '30 Minute Meeting' });
    expect(pageLink).not.toHaveClass('bg-accent-50');
    expect(within(nav()).getByRole('link', { name: 'Working hours' })).toHaveAttribute('aria-current', 'page');
    // ...and the page's own settings are not expanded under it.
    expect(within(nav()).queryByRole('link', { name: 'Booking limits' })).not.toBeInTheDocument();
  });

  it('points organizer-wide settings at a real page even when none is in scope', async () => {
    renderAt('/dashboard/analytics');

    // The routes need a page id; which one is immaterial, so the first owned
    // page is borrowed rather than the link being dead.
    expect(await within(nav()).findByRole('link', { name: 'Working hours' }))
      .toHaveAttribute('href', `/dashboard/${PAGE_ID}/settings/hours`);
  });

  it('shows organizer-wide settings as unavailable when no booking page exists yet', async () => {
    // These routes need a page id to build, and there is none to borrow.
    vi.spyOn(api.organizer, 'getMyBookingPages').mockResolvedValue([]);
    renderAt('/dashboard');

    expect(await screen.findByText('No booking pages yet.')).toBeInTheDocument();
    expect(within(nav()).queryByRole('link', { name: 'Working hours' })).not.toBeInTheDocument();
    expect(within(nav()).getByText('Working hours')).toHaveAttribute('title', 'Create a booking page first.');
  });

  it('does not present a still-loading page list as an unavailable feature', async () => {
    // Both states leave the workspace links unbuildable, but only one of them
    // means "you cannot do this" - greying the group during a 200ms fetch reads
    // as the whole section being switched off.
    vi.spyOn(api.organizer, 'getMyBookingPages').mockReturnValue(new Promise(() => {}));
    renderAt('/dashboard');

    const pending = within(nav()).getByText('Working hours');
    expect(pending).not.toHaveAttribute('title');
    expect(pending).not.toHaveClass('text-gray-400');
  });

  it('fetches the booking page list once, not once per screen', async () => {
    const fetchPages = vi.spyOn(api.organizer, 'getMyBookingPages');
    renderAt(`/dashboard/${PAGE_ID}/settings/hours`);

    await waitFor(() => expect(fetchPages).toHaveBeenCalled());
    expect(fetchPages).toHaveBeenCalledTimes(1);
  });

  it('still navigates when the page list cannot be loaded', async () => {
    vi.spyOn(api.organizer, 'getMyBookingPages').mockRejectedValue(new Error('offline'));
    renderAt(`/dashboard/${PAGE_ID}/settings/hours`);

    expect(await screen.findByText('No booking pages yet.')).toBeInTheDocument();
    expect(within(nav()).getByRole('link', { name: 'Dashboard' })).toBeInTheDocument();
  });

  describe('narrow screens', () => {
    it('opens and closes the navigation drawer', async () => {
      const { user } = renderAt(`/dashboard/${PAGE_ID}`);

      const open = screen.getByRole('button', { name: 'Open navigation' });
      expect(open).toHaveAttribute('aria-expanded', 'false');

      await user.click(open);
      expect(open).toHaveAttribute('aria-expanded', 'true');
      // Both the permanent rail and the drawer copy are mounted; the rail is
      // hidden by a media query jsdom does not apply, so there are two.
      expect(screen.getAllByRole('navigation', { name: 'Main' })).toHaveLength(2);

      await user.click(screen.getByRole('button', { name: 'Close navigation' }));
      expect(open).toHaveAttribute('aria-expanded', 'false');
    });

    it('closes on Escape, which is the expected way out of an overlay', async () => {
      const { user } = renderAt(`/dashboard/${PAGE_ID}`);
      await user.click(screen.getByRole('button', { name: 'Open navigation' }));

      await user.keyboard('{Escape}');

      expect(screen.getByRole('button', { name: 'Open navigation' })).toHaveAttribute('aria-expanded', 'false');
    });

    it('moves focus into the drawer when it opens', async () => {
      const { user } = renderAt(`/dashboard/${PAGE_ID}`);

      await user.click(screen.getByRole('button', { name: 'Open navigation' }));

      // Otherwise the next Tab continues behind the overlay.
      expect(screen.getByRole('button', { name: 'Close navigation' })).toHaveFocus();
    });

    it('closes itself when a link inside it is followed', async () => {
      const { user } = renderAt(`/dashboard/${PAGE_ID}`);
      await user.click(screen.getByRole('button', { name: 'Open navigation' }));

      const drawer = screen.getAllByRole('navigation', { name: 'Main' })[1];
      await user.click(within(drawer).getByRole('link', { name: 'Analytics' }));

      expect(screen.getByRole('button', { name: 'Open navigation' })).toHaveAttribute('aria-expanded', 'false');
    });
  });
});
