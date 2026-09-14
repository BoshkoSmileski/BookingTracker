import { render, screen, waitFor, waitForElementToBeRemoved } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactElement } from 'react';
import { Link, MemoryRouter, Route, Routes } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { BookingPageDetailsPage } from '../BookingPageDetailsPage';
import { NotificationSettingsPage } from '../NotificationSettingsPage';
import { WorkingHoursPage } from '../WorkingHoursPage';
import { ApiError, api } from '../../lib/api';
import { bookingPageDetail, notificationSettings, workingSchedule } from '../../test/factories';
import { resetAuthState } from '../../test/authContextMock';
import { LEAVE_LABEL, STAY_LABEL, UNSAVED_CHANGES_TITLE } from '../../hooks/useUnsavedChanges';

vi.mock('../../contexts/AuthContext', () => import('../../test/authContextMock'));

/**
 * The unsaved-changes guard, on the real settings screens.
 *
 * `useUnsavedChanges` has its own tests for the mechanism; what is pinned here
 * is the wiring, which is the half that can silently do nothing. A page that
 * forgets `markSaved()` after its load never takes a baseline, so it is never
 * dirty and the guard is a no-op that no other test would notice - which is the
 * exact failure mode the load-error contract was written about, one screen further
 * out.
 *
 * `renderWithProviders` cannot host these: it supplies a router with a single
 * route, and there has to be somewhere to navigate *to* for a held navigation
 * to be observable.
 */

const PAGE_ID = '11111111-1111-1111-1111-111111111111';
const AWAY = '/dashboard/analytics';

/** The page under test, plus the kind of rail link that used to lose the edits. */
function renderWithRail(ui: ReactElement, { path, route }: { path: string; route: string }) {
  const user = userEvent.setup();
  return {
    user,
    ...render(
      <MemoryRouter initialEntries={[route]}>
        <Routes>
          <Route
            path={path}
            element={
              <>
                {ui}
                <Link to={AWAY}>Analytics</Link>
              </>
            }
          />
          <Route path={AWAY} element={<h1>Analytics</h1>} />
        </Routes>
      </MemoryRouter>,
    ),
  };
}

const railLink = () => screen.getByRole('link', { name: 'Analytics' });
const arrived = () => screen.queryByRole('heading', { name: 'Analytics' });
const promptShown = () => screen.queryByRole('group', { name: UNSAVED_CHANGES_TITLE });

beforeEach(() => {
  resetAuthState();
});

describe('WorkingHoursPage — unsaved changes', () => {
  async function renderLoaded() {
    vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(workingSchedule());
    const rendered = renderWithRail(<WorkingHoursPage />, {
      path: '/dashboard/:pageId/settings/hours',
      route: `/dashboard/${PAGE_ID}/settings/hours`,
    });
    await screen.findByRole('checkbox', { name: 'Monday' });
    return rendered;
  }

  it('does not warn about a week it only just loaded', async () => {
    const { user } = await renderLoaded();

    await user.click(railLink());

    // The whole risk of a baseline taken at mount: `emptyWeek()` is seven
    // closed days, so the load itself would look like seven edits.
    expect(promptShown()).not.toBeInTheDocument();
    expect(arrived()).toBeInTheDocument();
  });

  it('holds a rail link once a day has been changed', async () => {
    const { user } = await renderLoaded();
    await user.click(screen.getByRole('checkbox', { name: 'Saturday' }));

    await user.click(railLink());

    expect(await screen.findByRole('group', { name: UNSAVED_CHANGES_TITLE })).toBeInTheDocument();
    expect(arrived()).not.toBeInTheDocument();
  });

  it('Stay keeps the whole week exactly as it was edited', async () => {
    const { user } = await renderLoaded();
    await user.click(screen.getByRole('checkbox', { name: 'Saturday' }));
    await user.click(railLink());

    await user.click(await screen.findByRole('button', { name: STAY_LABEL }));

    expect(arrived()).not.toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'Saturday' })).toBeChecked();
    expect(promptShown()).not.toBeInTheDocument();
  });

  it('Leave abandons the edits and goes where the link pointed', async () => {
    const { user } = await renderLoaded();
    await user.click(screen.getByRole('checkbox', { name: 'Saturday' }));
    await user.click(railLink());

    await user.click(await screen.findByRole('button', { name: LEAVE_LABEL }));

    expect(await screen.findByRole('heading', { name: 'Analytics' })).toBeInTheDocument();
  });

  it('stops warning once the week has been saved', async () => {
    const saved = workingSchedule({
      days: workingSchedule().days.map((d) => (d.dayOfWeek === 6 ? { ...d, isEnabled: true } : d)),
    });
    vi.spyOn(api.availability, 'saveSchedule').mockResolvedValue(saved);
    const { user } = await renderLoaded();
    await user.click(screen.getByRole('checkbox', { name: 'Saturday' }));

    await user.click(screen.getByRole('button', { name: 'Save schedule' }));
    await screen.findByText('Saved.');
    await user.click(railLink());

    // The response is re-seeded into the form, and that is the new baseline -
    // so what is on screen is what a reload would show, and nothing is pending.
    expect(promptShown()).not.toBeInTheDocument();
    expect(arrived()).toBeInTheDocument();
  });

  it('keeps warning when the save was rejected', async () => {
    vi.spyOn(api.availability, 'saveSchedule').mockRejectedValue(
      new ApiError(400, { title: 'Validation failed', errors: { TimeZoneId: ['Not a zone.'] } }),
    );
    const { user } = await renderLoaded();
    await user.click(screen.getByRole('checkbox', { name: 'Saturday' }));

    await user.click(screen.getByRole('button', { name: 'Save schedule' }));
    await screen.findByText('Not a zone.');
    await user.click(railLink());

    // A rejected save is the moment the guard matters most: the week is still
    // only in the browser, and pressing Save is not the same as having saved.
    expect(await screen.findByRole('group', { name: UNSAVED_CHANGES_TITLE })).toBeInTheDocument();
    expect(arrived()).not.toBeInTheDocument();
  });
});

describe('NotificationSettingsPage — unsaved changes', () => {
  async function renderLoaded() {
    vi.spyOn(api.organizer, 'getNotificationSettings').mockResolvedValue(notificationSettings());
    const rendered = renderWithRail(<NotificationSettingsPage />, {
      path: '/dashboard/:pageId/settings/notifications',
      route: `/dashboard/${PAGE_ID}/settings/notifications`,
    });
    await waitForElementToBeRemoved(() => screen.queryByLabelText('Loading'));
    return rendered;
  }

  it('does not warn about settings it only just loaded', async () => {
    const { user } = await renderLoaded();

    await user.click(railLink());

    // This page initialises to the backend's own defaults, so a mount-time
    // baseline would be right by coincidence here and wrong for any organizer
    // who had changed anything.
    expect(promptShown()).not.toBeInTheDocument();
    expect(arrived()).toBeInTheDocument();
  });

  it('holds a rail link once a toggle has been changed', async () => {
    const { user } = await renderLoaded();
    await user.click(screen.getByRole('checkbox', { name: /guest notifications/i }));

    await user.click(railLink());

    expect(await screen.findByRole('group', { name: UNSAVED_CHANGES_TITLE })).toBeInTheDocument();
  });

  it('goes quiet again when the toggle is put back', async () => {
    const { user } = await renderLoaded();
    const guest = screen.getByRole('checkbox', { name: /guest notifications/i });

    await user.click(guest);
    await user.click(guest);
    await user.click(railLink());

    // Reverting an edit is not an edit. Without a real comparison against the
    // loaded values this would warn about a form identical to the server's.
    expect(promptShown()).not.toBeInTheDocument();
    expect(arrived()).toBeInTheDocument();
  });

  it('stops warning once the settings have been saved', async () => {
    vi.spyOn(api.organizer, 'updateNotificationSettings').mockResolvedValue(
      notificationSettings({ notifyGuestOnBooking: false }),
    );
    const { user } = await renderLoaded();
    await user.click(screen.getByRole('checkbox', { name: /guest notifications/i }));

    await user.click(screen.getByRole('button', { name: /save/i }));
    await screen.findByText(/notification settings saved/i);
    await user.click(railLink());

    expect(promptShown()).not.toBeInTheDocument();
    expect(arrived()).toBeInTheDocument();
  });

  it('keeps warning when the save was rejected', async () => {
    vi.spyOn(api.organizer, 'updateNotificationSettings').mockRejectedValue(
      new ApiError(400, { title: 'Validation failed', errors: { '': ['Too many intervals.'] } }),
    );
    const { user } = await renderLoaded();
    await user.click(screen.getByRole('checkbox', { name: /guest notifications/i }));

    await user.click(screen.getByRole('button', { name: /save/i }));
    await screen.findByText('Too many intervals.');
    await user.click(railLink());

    expect(await screen.findByRole('group', { name: UNSAVED_CHANGES_TITLE })).toBeInTheDocument();
  });
});

describe('BookingPageDetailsPage — unsaved changes', () => {
  async function renderLoaded() {
    vi.spyOn(api.organizer, 'getBookingPage').mockResolvedValue(
      bookingPageDetail({ id: PAGE_ID, title: 'Discovery call', description: 'A chat.' }),
    );
    const rendered = renderWithRail(<BookingPageDetailsPage />, {
      path: '/dashboard/:pageId/settings/details',
      route: `/dashboard/${PAGE_ID}/settings/details`,
    });
    await screen.findByDisplayValue('Discovery call');
    return rendered;
  }

  it('does not warn about a title it only just loaded', async () => {
    const { user } = await renderLoaded();

    await user.click(railLink());

    expect(promptShown()).not.toBeInTheDocument();
    expect(arrived()).toBeInTheDocument();
  });

  it('holds a rail link once the title has been edited, and Stay keeps the text', async () => {
    const { user } = await renderLoaded();
    await user.type(screen.getByLabelText('Title'), ' v2');

    await user.click(railLink());
    await user.click(await screen.findByRole('button', { name: STAY_LABEL }));

    expect(arrived()).not.toBeInTheDocument();
    expect(screen.getByLabelText('Title')).toHaveValue('Discovery call v2');
  });

  it('keeps warning when the save was rejected', async () => {
    vi.spyOn(api.organizer, 'updateBookingPageDetails').mockRejectedValue(
      new ApiError(400, { title: 'Validation failed', errors: { Title: ['Too long.'] } }),
    );
    const { user } = await renderLoaded();
    await user.type(screen.getByLabelText('Title'), ' v2');

    await user.click(screen.getByRole('button', { name: 'Save' }));
    await screen.findByText('Too long.');
    await user.click(railLink());

    expect(await screen.findByRole('group', { name: UNSAVED_CHANGES_TITLE })).toBeInTheDocument();
  });

  it('stops warning once the details have been saved', async () => {
    vi.spyOn(api.organizer, 'updateBookingPageDetails').mockResolvedValue(bookingPageDetail({ id: PAGE_ID, title: 'Discovery call v2' }));
    const { user } = await renderLoaded();
    await user.type(screen.getByLabelText('Title'), ' v2');

    await user.click(screen.getByRole('button', { name: 'Save' }));
    await screen.findByText('Saved.');
    await user.click(railLink());

    // This page does not re-seed from the response - what was typed IS what was
    // persisted - so the baseline has to be taken from the form itself.
    await waitFor(() => expect(arrived()).toBeInTheDocument());
    expect(promptShown()).not.toBeInTheDocument();
  });
});
