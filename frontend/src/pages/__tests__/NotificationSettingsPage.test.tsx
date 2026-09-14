import { screen, waitFor, waitForElementToBeRemoved, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { NotificationSettingsPage } from '../NotificationSettingsPage';
import { api } from '../../lib/api';
import { notificationSettings } from '../../test/factories';
import { renderWithProviders } from '../../test/render';
import { resetAuthState } from '../../test/authContextMock';

vi.mock('../../contexts/AuthContext', () => import('../../test/authContextMock'));

/**
 * REGRESSION AREA: notification settings persistence and reminder preview
 * generation. Drives the page the way an organizer does - ticking checkboxes and
 * pressing Save - and asserts on what is rendered, not on component internals.
 */
describe('NotificationSettingsPage', () => {
  const PAGE_ID = '11111111-1111-1111-1111-111111111111';

  const renderPage = () =>
    renderWithProviders(<NotificationSettingsPage />, {
      route: `/dashboard/${PAGE_ID}/settings/notifications`,
      path: '/dashboard/:pageId/settings/notifications',
    });

  beforeEach(() => {
    resetAuthState();
  });

  async function renderLoaded(settings = notificationSettings()) {
    vi.spyOn(api.organizer, 'getNotificationSettings').mockResolvedValue(settings);
    const result = renderPage();
    await waitForElementToBeRemoved(() => screen.queryByLabelText('Loading'));
    return result;
  }

  it('shows a loading state before settings arrive', async () => {
    vi.spyOn(api.organizer, 'getNotificationSettings').mockResolvedValue(notificationSettings());
    renderPage();

    expect(screen.getByLabelText('Loading')).toBeInTheDocument();
    await waitForElementToBeRemoved(() => screen.queryByLabelText('Loading'));
  });

  it('reflects the saved settings once loaded', async () => {
    await renderLoaded(notificationSettings({
      notifyGuestOnBooking: true, notifyOrganizerOnBooking: false, remindersEnabled: true,
      reminderMinutesBeforeEvent: [1440],
    }));

    expect(screen.getByRole('checkbox', { name: /guest notifications/i })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: /organizer notifications/i })).not.toBeChecked();
    expect(screen.getByRole('checkbox', { name: /24 hours before/i })).toBeChecked();
  });

  it('labels every control, so each checkbox is reachable by its accessible name', async () => {
    await renderLoaded();

    // Nothing here is a bare unlabelled input - a screen reader can name them all.
    for (const box of screen.getAllByRole('checkbox')) {
      expect(box).toHaveAccessibleName();
    }
  });

  it('hides the reminder controls when reminders are switched off', async () => {
    await renderLoaded(notificationSettings({ remindersEnabled: false }));

    expect(screen.queryByRole('checkbox', { name: /24 hours before/i })).not.toBeInTheDocument();
    expect(screen.queryByText(/guests will receive reminders/i)).not.toBeInTheDocument();
  });

  it('reveals the reminder controls when reminders are switched on', async () => {
    const { user } = await renderLoaded(notificationSettings({ remindersEnabled: false }));

    await user.click(screen.getByRole('checkbox', { name: /send reminder emails to guests/i }));

    expect(await screen.findByRole('checkbox', { name: /24 hours before/i })).toBeInTheDocument();
  });

  /**
   * The preview repeats the same wording as the checkboxes ("24 hours before"),
   * which is intentional - they must agree. Queries are therefore scoped to the
   * preview list rather than searching the whole page.
   */
  const previewList = () =>
    screen.getByText(/example based on a meeting tomorrow/i).parentElement!.querySelector('ul')!;

  it('previews concrete fire times for the selected reminders', async () => {
    // REGRESSION: the preview must update from the checkboxes, not from a stale copy.
    await renderLoaded(notificationSettings({ reminderMinutesBeforeEvent: [1440] }));

    const preview = within(previewList());
    expect(preview.getByText(/24 hours before/)).toBeInTheDocument();
    // A concrete clock time, not just the relative label.
    expect(preview.getByText(/\d{1,2}:\d{2}/)).toBeInTheDocument();
  });

  it('updates the preview immediately when a reminder is ticked', async () => {
    const { user } = await renderLoaded(notificationSettings({ reminderMinutesBeforeEvent: [1440] }));
    expect(within(previewList()).getAllByRole('listitem')).toHaveLength(1);

    await user.click(screen.getByRole('checkbox', { name: /1 hour before/i }));

    await waitFor(() => expect(within(previewList()).getAllByRole('listitem')).toHaveLength(2));
    expect(within(previewList()).getByText(/1 hour before/)).toBeInTheDocument();
  });

  it('saves exactly what the organizer selected', async () => {
    const update = vi.spyOn(api.organizer, 'updateNotificationSettings')
      .mockResolvedValue(notificationSettings({ reminderMinutesBeforeEvent: [60, 1440] }));
    const { user } = await renderLoaded(notificationSettings({ reminderMinutesBeforeEvent: [1440] }));

    await user.click(screen.getByRole('checkbox', { name: /1 hour before/i }));
    await user.click(screen.getByRole('button', { name: /save settings/i }));

    await waitFor(() => expect(update).toHaveBeenCalled());
    const [, payload] = update.mock.calls[0];
    // Sorted ascending, matching what the backend stores.
    expect(payload.reminderMinutesBeforeEvent).toEqual([60, 1440]);
    expect(payload.notifyGuestOnBooking).toBe(true);
  });

  it('confirms a successful save, mentioning that existing bookings were updated', async () => {
    vi.spyOn(api.organizer, 'updateNotificationSettings').mockResolvedValue(notificationSettings());
    const { user } = await renderLoaded();

    await user.click(screen.getByRole('button', { name: /save settings/i }));

    expect(await screen.findByText(/notification settings saved/i)).toBeInTheDocument();
    expect(screen.getByText(/upcoming bookings have been updated/i)).toBeInTheDocument();
  });

  it('keeps the save confirmation on screen instead of timing it out', async () => {
    // It used to clear itself after five seconds. The organizer is still on the
    // page, may have looked away while it saved, and "upcoming bookings have
    // been updated" is not a glance-at-it fact - and the same timer took errors
    // away too.
    vi.spyOn(api.organizer, 'updateNotificationSettings').mockResolvedValue(notificationSettings());
    const { user } = await renderLoaded();

    await user.click(screen.getByRole('button', { name: /save settings/i }));
    await screen.findByText(/notification settings saved/i);

    await new Promise((resolve) => setTimeout(resolve, 60));
    expect(screen.getByText(/notification settings saved/i)).toBeInTheDocument();
  });

  describe('reminder presets', () => {
    it('marks a chosen interval with the app accent rather than the old near-black', async () => {
      // The selected tile was `border-gray-900 bg-gray-50` - pre-accent - while
      // the checkbox inside it was already teal, so one control carried two
      // colour languages and the tile read as disabled.
      await renderLoaded(notificationSettings({ reminderMinutesBeforeEvent: [1440] }));

      const chosen = screen.getByRole('checkbox', { name: /24 hours before/i });
      const tile = chosen.closest('label') as HTMLElement;
      expect(chosen).toBeChecked();
      expect(tile.className).toMatch(/border-accent-600/);
      expect(tile.className).not.toMatch(/border-gray-900/);
    });

    it('leaves an unchosen interval unaccented', async () => {
      await renderLoaded(notificationSettings({ reminderMinutesBeforeEvent: [1440] }));

      const other = screen.getByRole('checkbox', { name: /1 hour before/i });
      expect(other).not.toBeChecked();
      expect((other.closest('label') as HTMLElement).className).not.toMatch(/border-accent-600/);
    });

    it('moves the accent as the choice changes', async () => {
      const { user } = await renderLoaded(notificationSettings({ reminderMinutesBeforeEvent: [1440] }));

      await user.click(screen.getByRole('checkbox', { name: /24 hours before/i }));

      const tile = screen.getByRole('checkbox', { name: /24 hours before/i }).closest('label') as HTMLElement;
      expect(tile.className).not.toMatch(/border-accent-600/);
    });

    it('carries keyboard focus on the whole tile, not just the 18px box', async () => {
      await renderLoaded();

      const tile = screen.getByRole('checkbox', { name: /24 hours before/i }).closest('label') as HTMLElement;
      expect(tile.className).toMatch(/has-\[:focus-visible\]:ring-2/);
    });

    it('disables the rest at the limit, and says so, without hiding them', async () => {
      // Enforced with `disabled` rather than a click handler that silently does
      // nothing - so the control announces its own state.
      // Eight chosen, one of them a saved custom value, so a preset is left
      // over to be blocked - there are exactly as many presets as the limit
      // allows, so ticking all eight presets leaves nothing to assert on.
      await renderLoaded(notificationSettings({ reminderMinutesBeforeEvent: [5, 15, 30, 60, 120, 360, 720, 1440] }));

      const blocked = screen.getByRole('checkbox', { name: /2 days before/i });
      expect(blocked).toBeDisabled();
      expect(screen.getByRole('checkbox', { name: /24 hours before/i })).toBeEnabled();
      expect(screen.getByText(/limit reached/i)).toBeInTheDocument();
    });

    it('names the eight tiles as one group, and carries the limit note into it', async () => {
      // Each tile announces only its own "24 hours before". The caption above
      // them and the limit note below them were bare paragraphs, so neither
      // reached anyone using a screen reader.
      await renderLoaded();

      const group = screen.getByRole('group', { name: 'Send a reminder' });
      expect(within(group).getByRole('checkbox', { name: /24 hours before/i })).toBeInTheDocument();

      const describedBy = group.getAttribute('aria-describedby');
      expect(describedBy).toBeTruthy();
      expect(document.getElementById(describedBy as string)).toHaveTextContent(/Up to 8 reminders per booking/i);
    });
  });

  it('reports a failed save instead of silently doing nothing', async () => {
    vi.spyOn(api.organizer, 'updateNotificationSettings').mockRejectedValue(new Error('boom'));
    const { user } = await renderLoaded();

    await user.click(screen.getByRole('button', { name: /save settings/i }));

    expect(await screen.findByText(/failed to save notification settings/i)).toBeInTheDocument();
  });

  it('blocks saving an invalid selection rather than letting the server reject it', async () => {
    // Reminders on with nothing ticked is not saveable; the button is disabled and
    // the reason is shown.
    const update = vi.spyOn(api.organizer, 'updateNotificationSettings');
    const { user } = await renderLoaded(notificationSettings({ reminderMinutesBeforeEvent: [1440] }));

    await user.click(screen.getByRole('checkbox', { name: /24 hours before/i }));

    expect(screen.getByText(/pick at least one reminder time/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /save settings/i })).toBeDisabled();
    expect(update).not.toHaveBeenCalled();
  });

  it('keeps a saved non-preset interval visible so it is not silently dropped', async () => {
    // A custom value (90 minutes) has no preset checkbox; it must still appear,
    // or toggling something else would discard it on the next save.
    await renderLoaded(notificationSettings({ reminderMinutesBeforeEvent: [90, 1440] }));

    expect(screen.getByRole('checkbox', { name: /90 minutes before/i })).toBeChecked();
  });

  it('offers the organizer reminder-copy opt-in, off by default', async () => {
    await renderLoaded();

    expect(screen.getByRole('checkbox', { name: /copy me on reminder emails/i })).not.toBeChecked();
  });

  it('supports keyboard toggling of a reminder checkbox', async () => {
    const { user } = await renderLoaded(notificationSettings({ reminderMinutesBeforeEvent: [1440] }));
    const box = screen.getByRole('checkbox', { name: /1 hour before/i });

    box.focus();
    expect(box).toHaveFocus();
    await user.keyboard(' ');

    expect(box).toBeChecked();
  });
});

/**
 * The form's own state is initialised to the backend's defaults (everything on,
 * one 24h reminder), so falling through to it on a failed load would have shown
 * settings that look real - and saved over whatever the organizer actually had.
 * It skeletoned for ever instead; now it says so.
 */
describe('NotificationSettingsPage — the settings could not be loaded', () => {
  const PAGE_ID = '11111111-1111-1111-1111-111111111111';
  const renderPage = () =>
    renderWithProviders(<NotificationSettingsPage />, {
      route: `/dashboard/${PAGE_ID}/settings/notifications`,
      path: '/dashboard/:pageId/settings/notifications',
    });

  beforeEach(() => {
    resetAuthState();
  });

  it('leaves the skeleton and reports the failure', async () => {
    vi.spyOn(api.organizer, 'getNotificationSettings').mockRejectedValue(new Error('boom'));
    renderPage();

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.queryByRole('status', { name: 'Loading' })).not.toBeInTheDocument();
  });

  it('never shows the form over a load that failed', async () => {
    vi.spyOn(api.organizer, 'getNotificationSettings').mockRejectedValue(new Error('boom'));
    renderPage();

    await screen.findByRole('alert');
    expect(screen.queryByRole('button', { name: /save settings/i })).not.toBeInTheDocument();
  });

  it('retries and renders the saved settings when the retry succeeds', async () => {
    const get = vi.spyOn(api.organizer, 'getNotificationSettings')
      .mockRejectedValueOnce(new Error('boom'))
      .mockResolvedValueOnce(notificationSettings({ notifyGuestOnBooking: false }));
    const { user } = renderPage();

    await screen.findByRole('alert');
    await user.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByRole('checkbox', { name: /guest notifications/i })).not.toBeChecked();
    expect(get).toHaveBeenCalledTimes(2);
  });
});
