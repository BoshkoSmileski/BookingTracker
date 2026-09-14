import { screen, waitFor, waitForElementToBeRemoved, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { CalendarIntegrationPage } from '../CalendarIntegrationPage';
import { ApiError, NetworkError, api } from '../../lib/api';
import { calendarConnection } from '../../test/factories';
import { formatDateTime } from '../../lib/dates';
import { renderWithProviders } from '../../test/render';
import { resetAuthState } from '../../test/authContextMock';

vi.mock('../../contexts/AuthContext', () => import('../../test/authContextMock'));

/**
 * REGRESSION AREA: calendar sync UI, including the timestamp bug where
 * lastSuccessfulSyncAtUtc was parsed with `new Date(value)` instead of going
 * through lib/dates.ts - rendering 18:15 UTC as 18:15 local instead of 20:15
 * in UTC+2.
 */
describe('CalendarIntegrationPage', () => {
  const PAGE_ID = '11111111-1111-1111-1111-111111111111';

  const renderPage = () =>
    renderWithProviders(<CalendarIntegrationPage />, {
      route: `/dashboard/${PAGE_ID}/settings/calendar`,
      path: '/dashboard/:pageId/settings/calendar',
    });

  beforeEach(() => {
    resetAuthState();
  });

  async function renderLoaded(connection = calendarConnection()) {
    vi.spyOn(api.calendar, 'getConnection').mockResolvedValue(connection);
    const result = renderPage();
    await waitForElementToBeRemoved(() => screen.queryByLabelText('Loading'));
    return result;
  }

  it('offers a connect button when no calendar is linked', async () => {
    vi.spyOn(api.calendar, 'getConnection').mockResolvedValue(null);
    renderPage();
    await waitForElementToBeRemoved(() => screen.queryByLabelText('Loading'));

    expect(screen.getByRole('button', { name: /connect google calendar/i })).toBeInTheDocument();
    expect(screen.getByText(/no calendar connected yet/i)).toBeInTheDocument();
    expect(screen.getByText('Not connected')).toBeInTheDocument();
  });

  it('shows the connected account, calendar and synced count', async () => {
    await renderLoaded();

    expect(screen.getByText('organizer@gmail.com')).toBeInTheDocument();
    expect(screen.getByText('Work')).toBeInTheDocument();
    expect(screen.getByText('7')).toBeInTheDocument();
  });

  it('renders sync timestamps through the shared UTC parser', async () => {
    // REGRESSION: an offset-less backend timestamp is UTC. Comparing against
    // formatDateTime (not a hardcoded clock) keeps this true in any timezone.
    await renderLoaded(calendarConnection({ lastSuccessfulSyncAtUtc: '2026-08-04T18:15:00' }));

    expect(screen.getByText(formatDateTime('2026-08-04T18:15:00'))).toBeInTheDocument();
  });

  it('shows "Never" rather than a blank when a sync has not happened', async () => {
    await renderLoaded(calendarConnection({ lastSuccessfulSyncAtUtc: null, lastFailedSyncAtUtc: null }));

    expect(screen.getAllByText('Never')).toHaveLength(2);
  });

  it('spells out the Google Meet consequence of having no connection', async () => {
    // Not derivable from anywhere else on this screen: a Meet link is a side
    // effect of exporting the booking to the calendar, so with no calendar a
    // page set to Google Meet quietly produces none.
    vi.spyOn(api.calendar, 'getConnection').mockResolvedValue(null);
    renderPage();
    await waitForElementToBeRemoved(() => screen.queryByLabelText('Loading'));

    expect(screen.getByText(/google meet takes bookings without a meeting link/i)).toBeInTheDocument();
  });

  it('names the Google Meet consequence before disconnecting, in the page rather than a browser dialog', async () => {
    // Was `window.confirm`, which ran three separate consequences together into
    // one unstyled string and is truncated by some browsers. The Meet one is the
    // whole reason to ask: disconnecting silently stops links being created.
    const disconnect = vi.spyOn(api.calendar, 'disconnect');
    const { user } = await renderLoaded();

    await user.click(screen.getByRole('button', { name: /^disconnect$/i }));

    const panel = await screen.findByRole('group', { name: /disconnect google calendar/i });
    expect(within(panel).getByText(/google meet/i)).toBeInTheDocument();
    expect(within(panel).getByText(/no meeting link/i)).toBeInTheDocument();
    expect(within(panel).getByText(/stops blocking booking slots/i)).toBeInTheDocument();
    // Asking is not doing.
    expect(disconnect).not.toHaveBeenCalled();
  });

  it('backs out of the disconnect confirmation without disconnecting', async () => {
    const disconnect = vi.spyOn(api.calendar, 'disconnect');
    const { user } = await renderLoaded();

    await user.click(screen.getByRole('button', { name: /^disconnect$/i }));
    await user.click(await screen.findByRole('button', { name: /stay connected/i }));

    expect(disconnect).not.toHaveBeenCalled();
    expect(screen.queryByRole('group', { name: /disconnect google calendar/i })).not.toBeInTheDocument();
  });

  it('disconnects once confirmed', async () => {
    const disconnect = vi.spyOn(api.calendar, 'disconnect').mockResolvedValue(undefined);
    vi.spyOn(api.calendar, 'getConnection').mockResolvedValue(calendarConnection());
    const { user } = await renderLoaded();

    await user.click(screen.getByRole('button', { name: /^disconnect$/i }));
    const panel = await screen.findByRole('group', { name: /disconnect google calendar/i });
    await user.click(within(panel).getByRole('button', { name: /^disconnect$/i }));

    await waitFor(() => expect(disconnect).toHaveBeenCalled());
  });

  it('keeps a sync failure on screen instead of timing it out', async () => {
    // The notice used to remove itself after six seconds. A save confirmation
    // disappearing is merely inconsistent; an error an organizer has to act on
    // disappearing while they read the panel under it is a defect.
    vi.spyOn(api.calendar, 'syncNow').mockResolvedValue(
      calendarConnection({ status: 'Error', healthStatus: 'Synchronization Failed', lastSyncError: 'Google API returned 503' }),
    );
    const { user } = await renderLoaded();

    await user.click(screen.getByRole('button', { name: /sync now/i }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(/Google API returned 503/);
    await new Promise((resolve) => setTimeout(resolve, 50));
    expect(screen.getByRole('alert')).toHaveTextContent(/Google API returned 503/);
  });

  it('offers Sync Now and Disconnect on a healthy connection', async () => {
    await renderLoaded();

    expect(screen.getByRole('button', { name: /sync now/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^disconnect$/i })).toBeInTheDocument();
  });

  it('offers reconnect - not a fresh connect - when reauthorization is required', async () => {
    await renderLoaded(calendarConnection({ status: 'ReauthorizationRequired', healthStatus: 'Needs Reauthentication' }));

    expect(screen.getByRole('button', { name: /reconnect google calendar/i })).toBeInTheDocument();
    expect(screen.getByText('Needs Reauthentication')).toBeInTheDocument();
  });

  it('surfaces the most recent sync error', async () => {
    await renderLoaded(calendarConnection({
      status: 'Error', healthStatus: 'Synchronization Failed',
      lastSyncError: 'Google API returned 503', lastFailedSyncAtUtc: '2026-08-04T18:20:00',
    }));

    expect(screen.getByText('Google API returned 503')).toBeInTheDocument();
    expect(screen.getByText('Most recent error')).toBeInTheDocument();
  });

  it('reports a healthy result after Sync Now', async () => {
    vi.spyOn(api.calendar, 'syncNow').mockResolvedValue(calendarConnection());
    const { user } = await renderLoaded();

    await user.click(screen.getByRole('button', { name: /sync now/i }));

    expect(await screen.findByText(/sync complete/i)).toBeInTheDocument();
  });

  it('reports the sync error when Sync Now comes back unhealthy', async () => {
    // syncNow resolves even on failure - the DTO's status carries the outcome.
    vi.spyOn(api.calendar, 'syncNow').mockResolvedValue(calendarConnection({
      status: 'CalendarNotFound', healthStatus: 'Calendar Missing', lastSyncError: 'Calendar was deleted',
    }));
    const { user } = await renderLoaded();

    await user.click(screen.getByRole('button', { name: /sync now/i }));

    // Shown twice on purpose: as the transient notice and as the persistent
    // "Most recent error" field on the connection card.
    await waitFor(() => expect(screen.getAllByText('Calendar was deleted')).toHaveLength(2));
    expect(screen.getByText('Calendar Missing')).toBeInTheDocument();
  });

  it('loads and lists the organizer’s calendars on demand', async () => {
    vi.spyOn(api.calendar, 'getCalendars').mockResolvedValue([
      { id: 'primary', name: 'Work', isPrimary: true },
      { id: 'other', name: 'Personal', isPrimary: false },
    ]);
    const { user } = await renderLoaded();

    await user.click(screen.getByRole('button', { name: /refresh calendars/i }));

    expect(await screen.findByRole('option', { name: /work \(primary\)/i })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Personal' })).toBeInTheDocument();
  });

  it('labels the calendar picker, and constrains it so a long name cannot widen the row', async () => {
    // The select had no label at all - its Section heading says "Which
    // calendar", which a screen reader never associates with the control, so it
    // announced only its current value. And with no width of its own, a long
    // calendar name pushed the row past its container before it would wrap.
    vi.spyOn(api.calendar, 'getCalendars').mockResolvedValue([
      { id: 'primary', name: 'a-very-long-calendar-name@organizer.example — Work', isPrimary: true },
    ]);
    const { user } = await renderLoaded();

    await user.click(screen.getByRole('button', { name: /refresh calendars/i }));

    const picker = await screen.findByRole('combobox', { name: /calendar to sync with/i });
    // `min-w-0` is what lets a long option truncate inside the control instead
    // of forcing the flex row wider than its parent.
    expect(picker.className).toContain('min-w-0');
    // Full width on a phone, auto from `sm` - the app's existing convention.
    expect(picker.className).toContain('w-full');
    expect(picker.className).toContain('sm:w-auto');
  });

  it('stacks the picker row on a phone instead of wrapping it arbitrarily', async () => {
    vi.spyOn(api.calendar, 'getCalendars').mockResolvedValue([
      { id: 'primary', name: 'Work', isPrimary: true },
    ]);
    const { user } = await renderLoaded();
    await user.click(screen.getByRole('button', { name: /refresh calendars/i }));

    const picker = await screen.findByRole('combobox', { name: /calendar to sync with/i });
    const row = picker.parentElement!;
    // Column by default, row from `sm`: at 320px the three controls used to
    // wrap one per line in an order that put the action above its subject.
    expect(row.className).toContain('flex-col');
    expect(row.className).toContain('sm:flex-row');
  });

  it('labels the custom event-title input and ties the placeholder hint to it', async () => {
    // A placeholder is not a label, and it disappears the moment anything is
    // typed. The hint below was already written for this input.
    const { user } = await renderLoaded();

    await user.selectOptions(screen.getByLabelText(/event title/i), 'custom');

    const custom = await screen.findByRole('textbox', { name: /custom event title format/i });
    const hint = screen.getByText(/placeholders/i);

    // Read off the DOM rather than naming an id. The id is generated by
    // `<Field>`'s `useId` now, and a test that hardcoded one would be pinning
    // an implementation detail instead of the association it cares about.
    expect(hint.id).not.toBe('');
    expect(custom).toHaveAttribute('aria-describedby', hint.id);
    // The select is the field's own control, so the same hint describes it
    // too. Named exactly: once the custom input is showing, /event title/i is
    // ambiguous between the two.
    expect(screen.getByLabelText('Event title format')).toHaveAttribute('aria-describedby', hint.id);
  });

  it('saves sync and event settings together', async () => {
    const syncSettings = vi.spyOn(api.calendar, 'updateSyncSettings').mockResolvedValue(calendarConnection());
    const eventSettings = vi.spyOn(api.calendar, 'updateEventSettings').mockResolvedValue(calendarConnection());
    const { user } = await renderLoaded();

    await user.click(screen.getByRole('checkbox', { name: /import busy events/i }));
    await user.click(screen.getByRole('button', { name: /save settings/i }));

    await waitFor(() => expect(syncSettings).toHaveBeenCalled());
    expect(eventSettings).toHaveBeenCalled();
    // The toggle the organizer flipped is what gets sent.
    expect(syncSettings.mock.calls[0][1]).toBe(false);
  });

  it('reports a failed settings save', async () => {
    vi.spyOn(api.calendar, 'updateSyncSettings').mockRejectedValue(new Error('boom'));
    vi.spyOn(api.calendar, 'updateEventSettings').mockRejectedValue(new Error('boom'));
    const { user } = await renderLoaded();

    await user.click(screen.getByRole('button', { name: /save settings/i }));

    expect(await screen.findByText(/failed to save calendar settings/i)).toBeInTheDocument();
  });

  it('reports an unreachable server as connectivity, not as a rejected disconnect', async () => {
    // REGRESSION: five handlers on this screen caught the error and threw it
    // away, printing their own domain wording - so an API that was simply not
    // running reported "Failed to disconnect", sending debugging to the wrong
    // layer. Only the settings save had been routed through errorMessage.
    vi.spyOn(api.calendar, 'disconnect').mockRejectedValue(new NetworkError());
    const { user } = await renderLoaded();

    await user.click(screen.getByRole('button', { name: /^disconnect$/i }));
    const panel = await screen.findByRole('group', { name: /disconnect google calendar/i });
    await user.click(within(panel).getByRole('button', { name: /^disconnect$/i }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(/could not reach the server/i);
    expect(alert).not.toHaveTextContent(/failed to disconnect/i);
  });

  it('shows what the server said when loading calendars is rejected', async () => {
    vi.spyOn(api.calendar, 'getCalendars').mockRejectedValue(
      new ApiError(403, { title: 'Your Google authorization has expired.' }),
    );
    const { user } = await renderLoaded();

    await user.click(screen.getByRole('button', { name: /refresh calendars/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/your google authorization has expired/i);
  });

  it('labels the event-settings inputs', async () => {
    await renderLoaded();

    expect(screen.getByLabelText(/default reminder/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/event visibility/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/event title format/i)).toBeInTheDocument();
  });
});

/**
 * A failed connection read used to return `if (loading) return <skeleton>` for
 * ever - this screen never rendered at all. Resolving it to `null` instead
 * would have been worse: null is a real answer here meaning "no calendar
 * connected", so an organizer with a working connection would have been told
 * they had none.
 */
describe('CalendarIntegrationPage — the connection could not be loaded', () => {
  const PAGE_ID = '11111111-1111-1111-1111-111111111111';
  const renderPage = () =>
    renderWithProviders(<CalendarIntegrationPage />, {
      route: `/dashboard/${PAGE_ID}/settings/calendar`,
      path: '/dashboard/:pageId/settings/calendar',
    });

  beforeEach(() => {
    resetAuthState();
  });

  it('leaves the skeleton and reports the failure', async () => {
    vi.spyOn(api.calendar, 'getConnection').mockRejectedValue(new NetworkError(new Error('down')));
    renderPage();

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.queryByRole('status', { name: 'Loading' })).not.toBeInTheDocument();
  });

  it('does not say "No calendar connected yet" when it could not find out', async () => {
    vi.spyOn(api.calendar, 'getConnection').mockRejectedValue(new NetworkError(new Error('down')));
    renderPage();

    await screen.findByRole('alert');
    expect(screen.queryByText(/no calendar connected yet/i)).not.toBeInTheDocument();
  });

  it('keeps the page header, so the screen still says where you are', async () => {
    vi.spyOn(api.calendar, 'getConnection').mockRejectedValue(new NetworkError(new Error('down')));
    renderPage();

    await screen.findByRole('alert');
    expect(screen.getByRole('heading', { name: 'Calendar' })).toBeInTheDocument();
  });

  it('retries and renders the connection when the retry succeeds', async () => {
    const getConnection = vi.spyOn(api.calendar, 'getConnection')
      .mockRejectedValueOnce(new NetworkError(new Error('down')))
      .mockResolvedValueOnce(calendarConnection({ externalAccountEmail: 'org@example.com' }));
    const { user } = renderPage();

    await screen.findByRole('alert');
    await user.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByText('org@example.com')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(getConnection).toHaveBeenCalledTimes(2);
  });
});
