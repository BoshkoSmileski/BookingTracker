import { screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { DateExceptionsPage } from '../DateExceptionsPage';
import { NetworkError, api } from '../../lib/api';
import { availabilityException, availabilityOverride } from '../../test/factories';
import { renderWithProviders } from '../../test/render';
import { resetAuthState } from '../../test/authContextMock';
import type { AvailabilityExceptionDto, AvailabilityOverrideDto } from '../../lib/types';

vi.mock('../../contexts/AuthContext', () => import('../../test/authContextMock'));

/**
 * The consolidated availability-exceptions screen.
 *
 * This absorbs the coverage of the two pages it replaced - the date-specific
 * hours editor and the blocked-dates editor - because the underlying behaviour
 * of both is unchanged and must stay proven. What is new here is the
 * consolidation itself: one list in date order, and a form that says which of
 * the two things is being added.
 */
describe('DateExceptionsPage', () => {
  const PAGE_ID = '11111111-1111-1111-1111-111111111111';

  beforeEach(() => {
    resetAuthState();
  });

  async function renderLoaded(
    exceptions: AvailabilityExceptionDto[] = [],
    overrides: AvailabilityOverrideDto[] = [],
  ) {
    vi.spyOn(api.availability, 'getExceptions').mockResolvedValue(exceptions);
    vi.spyOn(api.availability, 'getOverrides').mockResolvedValue(overrides);
    const result = renderWithProviders(<DateExceptionsPage />, {
      route: `/dashboard/${PAGE_ID}/settings/date-exceptions`,
      path: '/dashboard/:pageId/settings/date-exceptions',
    });
    await waitFor(() => expect(api.availability.getOverrides).toHaveBeenCalled());
    return result;
  }

  /** Switches the form to the date-specific-hours branch, as an organizer does. */
  const chooseHours = (user: { click: (el: Element) => Promise<void> }) =>
    user.click(screen.getByRole('radio', { name: /different hours/i }));

  describe('one list for both kinds', () => {
    it('reads a month in date order regardless of which kind each entry is', async () => {
      // The workflow the two separate screens could not serve: "22nd I work
      // mornings, 25th-30th I am away" was two lists to cross-check.
      await renderLoaded(
        [availabilityException({ id: 'b1', date: '2026-08-25', endDate: '2026-08-30', totalDays: 6, reason: 'Summer vacation' })],
        [availabilityOverride({ id: 'h1', date: '2026-08-22', ranges: [{ start: '09:00:00', end: '12:00:00' }] })],
      );

      const rows = await screen.findAllByRole('listitem');
      expect(rows).toHaveLength(2);
      expect(within(rows[0]).getByText('09:00–12:00')).toBeInTheDocument();
      expect(within(rows[1]).getByText(/Summer vacation/)).toBeInTheDocument();
    });

    it('labels each row with which kind it is, since the list no longer says so', async () => {
      await renderLoaded([availabilityException()], [availabilityOverride()]);

      const rows = await screen.findAllByRole('listitem');
      expect(within(rows[0]).getByText('Different hours')).toBeInTheDocument();
      expect(within(rows[1]).getByText('Unavailable')).toBeInTheDocument();
    });

    it('fetches both kinds, so neither is missing from the list', async () => {
      await renderLoaded();

      expect(api.availability.getExceptions).toHaveBeenCalled();
      expect(api.availability.getOverrides).toHaveBeenCalled();
    });

    it('offers one empty state rather than one per kind', async () => {
      await renderLoaded();

      expect(await screen.findByText(/no exceptions yet/i)).toBeInTheDocument();
    });

    it('warns when hours are set on a date that is blocked outright', async () => {
      // Precedence is documented in the domain and was invisible on both old
      // screens: an organizer could set hours inside a vacation and see no slots
      // with no explanation.
      await renderLoaded(
        [availabilityException({ date: '2026-08-25', endDate: '2026-08-30', totalDays: 6 })],
        [availabilityOverride({ date: '2026-08-27' })],
      );

      expect(await screen.findByText(/have no effect - this date is blocked/i)).toBeInTheDocument();
    });

    it('does not warn about hours on a date nothing blocks', async () => {
      await renderLoaded([], [availabilityOverride({ date: '2026-08-27' })]);

      await screen.findByRole('listitem');
      expect(screen.queryByText(/have no effect/i)).not.toBeInTheDocument();
    });
  });

  describe('choosing what to add', () => {
    it('starts on blocking a date, the more common of the two', async () => {
      await renderLoaded();

      expect(screen.getByRole('radio', { name: /unavailable/i })).toBeChecked();
      expect(screen.getByLabelText('From')).toBeInTheDocument();
      expect(screen.queryByLabelText('Range 1 start')).not.toBeInTheDocument();
    });

    it('swaps the fields, and the submit action, for date-specific hours', async () => {
      const { user } = await renderLoaded();

      await chooseHours(user);

      expect(screen.getByLabelText('Date')).toBeInTheDocument();
      expect(screen.getByLabelText('Range 1 start')).toBeInTheDocument();
      expect(screen.queryByLabelText('From')).not.toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Save these hours' })).toBeInTheDocument();
    });

    it('explains what each choice does before it is made', async () => {
      const { user } = await renderLoaded();

      expect(screen.getByText(/nothing can be booked in it/i)).toBeInTheDocument();
      await chooseHours(user);
      expect(screen.getByText(/replaces your weekly hours on one date/i)).toBeInTheDocument();
    });
  });

  describe('blocking dates', () => {
    it('blocks a single day', async () => {
      const create = vi.spyOn(api.availability, 'createException').mockResolvedValue(availabilityException());
      const { user } = await renderLoaded();

      await user.type(screen.getByLabelText('From'), '2026-08-25');
      await user.type(screen.getByLabelText(/^to/i), '2026-08-30');
      await user.type(screen.getByLabelText(/^reason/i), 'Summer vacation');
      await user.click(screen.getByRole('button', { name: 'Block these dates' }));

      await waitFor(() =>
        expect(create).toHaveBeenCalledWith(
          expect.any(String), '2026-08-25', null, null, 'Vacation', 'Summer vacation', '2026-08-30',
        ),
      );
    });

    it('sends the window, not the whole day, once Whole day is cleared', async () => {
      const create = vi.spyOn(api.availability, 'createException').mockResolvedValue(availabilityException());
      const { user } = await renderLoaded();

      await user.type(screen.getByLabelText('From'), '2026-08-25');
      await user.click(screen.getByRole('checkbox', { name: /whole day/i }));
      await user.clear(screen.getByLabelText('Between'));
      await user.type(screen.getByLabelText('Between'), '12:00');
      await user.clear(screen.getByLabelText('and'));
      await user.type(screen.getByLabelText('and'), '13:00');
      await user.click(screen.getByRole('button', { name: 'Block these dates' }));

      await waitFor(() =>
        expect(create).toHaveBeenCalledWith(
          expect.any(String), '2026-08-25', '12:00:00', '13:00:00', 'Vacation', null, null,
        ),
      );
    });

    it('cannot submit an end date before the start', async () => {
      // The end date carries `min` from the start date, so the browser's own
      // picker never offers an earlier day and constraint validation blocks the
      // submit outright - which is why the identical check inside the handler
      // is a backstop rather than the thing being exercised here. The backend
      // enforces the same rule regardless of either.
      const create = vi.spyOn(api.availability, 'createException').mockResolvedValue(availabilityException());
      const { user } = await renderLoaded();

      await user.type(screen.getByLabelText('From'), '2026-08-25');
      await user.type(screen.getByLabelText(/^to/i), '2026-08-20');
      const to = screen.getByLabelText(/^to/i) as HTMLInputElement;
      expect(to).toHaveAttribute('min', '2026-08-25');
      expect(to.validity.rangeUnderflow).toBe(true);

      await user.click(screen.getByRole('button', { name: 'Block these dates' }));
      expect(create).not.toHaveBeenCalled();
    });

    it('deletes a blocked period', async () => {
      const remove = vi.spyOn(api.availability, 'deleteException').mockResolvedValue(undefined);
      const { user } = await renderLoaded([availabilityException({ id: 'blk' })]);

      await user.click(await screen.findByRole('button', { name: /delete blocked period/i }));
      // Destructive, so it asks first. The confirm
      // button's name is the bare action; the trigger's carries the period.
      await user.click(screen.getByRole('button', { name: 'Delete blocked period' }));

      expect(remove).toHaveBeenCalledWith(expect.any(String), 'blk');
    });

    it('does not delete a blocked period until the confirmation is accepted', async () => {
      const remove = vi.spyOn(api.availability, 'deleteException').mockResolvedValue(undefined);
      const { user } = await renderLoaded([availabilityException({ id: 'blk' })]);

      await user.click(await screen.findByRole('button', { name: /delete blocked period/i }));
      expect(remove).not.toHaveBeenCalled();

      await user.click(screen.getByRole('button', { name: 'Keep it' }));

      expect(remove).not.toHaveBeenCalled();
      // The row is still there, with its trigger back.
      expect(await screen.findByRole('button', { name: /delete blocked period/i })).toBeInTheDocument();
    });

    it('reports a failed delete instead of leaving the row silently in place', async () => {
      // REGRESSION: `deleteException` had no try/catch at all, so a rejection
      // was an unhandled promise - the period stayed listed and nothing said
      // why, which reads as a button that does nothing.
      vi.spyOn(api.availability, 'deleteException').mockRejectedValue(new NetworkError());
      const { user } = await renderLoaded([availabilityException({ id: 'blk' })]);

      await user.click(await screen.findByRole('button', { name: /delete blocked period/i }));
      await user.click(screen.getByRole('button', { name: 'Delete blocked period' }));

      expect(await screen.findByRole('alert')).toHaveTextContent(/could not reach/i);
      // The row it failed to remove is still there to try again on.
      expect(screen.getByRole('button', { name: /delete blocked period/i })).toBeInTheDocument();
    });
  });

  describe('date-specific hours', () => {
    it('lists existing hours with their note', async () => {
      await renderLoaded([], [availabilityOverride({ ranges: [{ start: '13:00:00', end: '17:00:00' }], note: 'Afternoon only' })]);

      expect(await screen.findByText(/afternoon only/i)).toBeInTheDocument();
      expect(screen.getByText('13:00–17:00')).toBeInTheDocument();
    });

    it('shows a closed day as closed rather than as empty hours', async () => {
      await renderLoaded([], [availabilityOverride({ isClosed: true, ranges: [] })]);

      // Scoped to the list: the form's own "Closed all day" checkbox
      // legitimately matches similar text, and loosening the query would hide that.
      const row = await screen.findByRole('listitem');
      expect(within(row).getByText('Closed all day')).toBeInTheDocument();
    });

    it('saves a new set of hours with the date and range the organizer entered', async () => {
      const save = vi.spyOn(api.availability, 'saveOverride').mockResolvedValue(availabilityOverride());
      const { user } = await renderLoaded();

      await chooseHours(user);
      await user.type(screen.getByLabelText('Date'), '2026-08-15');
      await user.clear(screen.getByLabelText('Range 1 start'));
      await user.type(screen.getByLabelText('Range 1 start'), '13:00');
      await user.click(screen.getByRole('button', { name: 'Save these hours' }));

      await waitFor(() =>
        expect(save).toHaveBeenCalledWith(
          expect.any(String), '2026-08-15', [{ start: '13:00:00', end: '17:00:00' }], null,
        ),
      );
    });

    it('sends an empty ranges array for a closed day', async () => {
      // Empty ranges IS "closed" on the wire - the same single source of truth
      // the entity uses, rather than a second flag that could contradict it.
      const save = vi.spyOn(api.availability, 'saveOverride').mockResolvedValue(availabilityOverride());
      const { user } = await renderLoaded();

      await chooseHours(user);
      await user.type(screen.getByLabelText('Date'), '2026-12-24');
      await user.click(screen.getByRole('checkbox', { name: /closed all day/i }));
      await user.click(screen.getByRole('button', { name: 'Save these hours' }));

      await waitFor(() => expect(save).toHaveBeenCalledWith(expect.any(String), '2026-12-24', [], null));
    });

    it('hides the hours editor once the day is marked closed', async () => {
      const { user } = await renderLoaded();

      await chooseHours(user);
      expect(screen.getByLabelText('Range 1 start')).toBeInTheDocument();
      await user.click(screen.getByRole('checkbox', { name: /closed all day/i }));

      expect(screen.queryByLabelText('Range 1 start')).not.toBeInTheDocument();
    });

    it('names the range rows as a group, rather than captioning them visually only', async () => {
      // Every input here is labelled "Range N start"/"Range N end" and nothing
      // else, so "Available hours" was a caption no screen reader could reach -
      // unlike the radio group at the top of the same form, which is a real
      // fieldset already.
      const { user } = await renderLoaded();
      await chooseHours(user);

      const group = screen.getByRole('group', { name: 'Available hours' });
      expect(within(group).getByLabelText('Range 1 start')).toBeInTheDocument();
      expect(within(group).getByLabelText('Range 1 end')).toBeInTheDocument();
    });

    it('supports several ranges in one day', async () => {
      const save = vi.spyOn(api.availability, 'saveOverride').mockResolvedValue(availabilityOverride());
      const { user } = await renderLoaded();

      await chooseHours(user);
      await user.type(screen.getByLabelText('Date'), '2026-08-20');
      await user.clear(screen.getByLabelText('Range 1 end'));
      await user.type(screen.getByLabelText('Range 1 end'), '12:00');
      await user.click(screen.getByRole('button', { name: /add another range/i }));
      await user.clear(screen.getByLabelText('Range 2 start'));
      await user.type(screen.getByLabelText('Range 2 start'), '13:00');
      await user.clear(screen.getByLabelText('Range 2 end'));
      await user.type(screen.getByLabelText('Range 2 end'), '15:00');
      await user.click(screen.getByRole('button', { name: 'Save these hours' }));

      await waitFor(() =>
        expect(save).toHaveBeenCalledWith(
          expect.any(String),
          '2026-08-20',
          [{ start: '09:00:00', end: '12:00:00' }, { start: '13:00:00', end: '15:00:00' }],
          null,
        ),
      );
    });

    it('loads existing hours into the form for editing, switching kind and locking the date', async () => {
      // The date identifies the override, so editing it here would create a
      // second one and leave the original behind.
      const { user } = await renderLoaded(
        [],
        [availabilityOverride({ date: '2026-08-15', ranges: [{ start: '13:00:00', end: '17:00:00' }] })],
      );

      await user.click(await screen.findByRole('button', { name: /edit hours for/i }));

      expect(screen.getByRole('radio', { name: /different hours/i })).toBeChecked();
      expect(screen.getByLabelText('Date')).toHaveValue('2026-08-15');
      expect(screen.getByLabelText('Date')).toHaveAttribute('readonly');
      expect(screen.getByLabelText('Range 1 start')).toHaveValue('13:00');
      expect(screen.getByRole('button', { name: 'Save changes' })).toBeInTheDocument();
    });

    it('abandons an edit in progress when the organizer switches to blocking a date', async () => {
      // The hours form is no longer on screen; leaving it half-filled is how a
      // later save writes to a date nobody is thinking about any more.
      const { user } = await renderLoaded([], [availabilityOverride({ date: '2026-08-15' })]);

      await user.click(await screen.findByRole('button', { name: /edit hours for/i }));
      await user.click(screen.getByRole('radio', { name: /unavailable/i }));
      await chooseHours(user);

      expect(screen.getByLabelText('Date')).toHaveValue('');
      expect(screen.getByRole('button', { name: 'Save these hours' })).toBeInTheDocument();
    });

    it('deletes a set of hours', async () => {
      const remove = vi.spyOn(api.availability, 'deleteOverride').mockResolvedValue(undefined);
      const { user } = await renderLoaded([], [availabilityOverride({ id: 'abc' })]);

      await user.click(await screen.findByRole('button', { name: /delete hours for/i }));
      await user.click(screen.getByRole('button', { name: 'Delete hours' }));

      expect(remove).toHaveBeenCalledWith(expect.any(String), 'abc');
    });

    it('does not delete hours until the confirmation is accepted', async () => {
      const remove = vi.spyOn(api.availability, 'deleteOverride').mockResolvedValue(undefined);
      const { user } = await renderLoaded([], [availabilityOverride({ id: 'abc' })]);

      await user.click(await screen.findByRole('button', { name: /delete hours for/i }));
      expect(remove).not.toHaveBeenCalled();

      await user.click(screen.getByRole('button', { name: 'Keep them' }));

      expect(remove).not.toHaveBeenCalled();
      expect(await screen.findByRole('button', { name: /delete hours for/i })).toBeInTheDocument();
    });

    it('reports a failed delete, and says which kind failed', async () => {
      // REGRESSION: same missing catch as the blocked-period delete. The
      // message names the hours rather than the form's own wording, because it
      // is reported beside the list, not above the submit button.
      vi.spyOn(api.availability, 'deleteOverride').mockRejectedValue(new Error('boom'));
      const { user } = await renderLoaded([], [availabilityOverride({ id: 'abc' })]);

      await user.click(await screen.findByRole('button', { name: /delete hours for/i }));
      await user.click(screen.getByRole('button', { name: 'Delete hours' }));

      expect(await screen.findByRole('alert')).toHaveTextContent(/could not remove these hours/i);
    });

    it('rejects a range that ends before it starts, without calling the API', async () => {
      const save = vi.spyOn(api.availability, 'saveOverride').mockResolvedValue(availabilityOverride());
      const { user } = await renderLoaded();

      await chooseHours(user);
      await user.type(screen.getByLabelText('Date'), '2026-08-15');
      await user.clear(screen.getByLabelText('Range 1 start'));
      await user.type(screen.getByLabelText('Range 1 start'), '17:00');
      await user.clear(screen.getByLabelText('Range 1 end'));
      await user.type(screen.getByLabelText('Range 1 end'), '09:00');
      await user.click(screen.getByRole('button', { name: 'Save these hours' }));

      expect(await screen.findByRole('alert')).toHaveTextContent(/start before it ends/i);
      expect(save).not.toHaveBeenCalled();
    });

    it('explains that hours replace the weekly schedule, and how to undo that', async () => {
      // The precedence rule stated where the organizer decides, rather than left
      // to be discovered from the slots a guest is offered afterwards. Deleting
      // and saving-empty are genuinely different operations and say so.
      const { user } = await renderLoaded();

      await chooseHours(user);

      expect(screen.getByText(/replaces your weekly hours on one date/i)).toBeInTheDocument();
      expect(screen.getByText(/delete them to go back to it/i)).toBeInTheDocument();
    });

    it('reports an unreachable server as connectivity, not as a rejected save', async () => {
      vi.spyOn(api.availability, 'saveOverride').mockRejectedValue(new NetworkError());
      const { user } = await renderLoaded();

      await chooseHours(user);
      await user.type(screen.getByLabelText('Date'), '2026-08-15');
      await user.click(screen.getByRole('button', { name: 'Save these hours' }));

      expect(await screen.findByRole('alert')).toHaveTextContent(/could not reach/i);
    });
  });

  it('keeps the page shape while loading instead of blanking it', async () => {
    vi.spyOn(api.availability, 'getExceptions').mockReturnValue(new Promise(() => {}));
    vi.spyOn(api.availability, 'getOverrides').mockReturnValue(new Promise(() => {}));
    renderWithProviders(<DateExceptionsPage />, {
      route: `/dashboard/${PAGE_ID}/settings/date-exceptions`,
      path: '/dashboard/:pageId/settings/date-exceptions',
    });

    expect(screen.getByRole('status', { name: 'Loading' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Date exceptions' })).toBeInTheDocument();
  });
});

/**
 * Initial-load failure versus refresh failure, which this screen is the clearest
 * case of: it already distinguishes a *rejected delete* (`listError`) from a
 * rejected save, and a list that never arrived is a third thing again.
 */
describe('DateExceptionsPage — the list could not be loaded', () => {
  beforeEach(() => {
    resetAuthState();
  });

  it('says so instead of skeletoning for ever', async () => {
    vi.spyOn(api.availability, 'getExceptions').mockRejectedValue(new NetworkError(new Error('down')));
    vi.spyOn(api.availability, 'getOverrides').mockResolvedValue([]);
    renderWithProviders(<DateExceptionsPage />, {
      route: '/dashboard/p1/settings/date-exceptions',
      path: '/dashboard/:pageId/settings/date-exceptions',
    });

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.queryByRole('status', { name: 'Loading' })).not.toBeInTheDocument();
  });

  it('does not claim there are no exceptions when it could not read them', async () => {
    // The whole reason this matters: "No exceptions yet" tells an organizer
    // their vacation is not blocked.
    vi.spyOn(api.availability, 'getExceptions').mockRejectedValue(new NetworkError(new Error('down')));
    vi.spyOn(api.availability, 'getOverrides').mockResolvedValue([]);
    renderWithProviders(<DateExceptionsPage />, {
      route: '/dashboard/p1/settings/date-exceptions',
      path: '/dashboard/:pageId/settings/date-exceptions',
    });

    await screen.findByRole('alert');
    expect(screen.queryByText('No exceptions yet')).not.toBeInTheDocument();
  });

  it('retries both endpoints and renders the list when the retry succeeds', async () => {
    const getExceptions = vi.spyOn(api.availability, 'getExceptions')
      .mockRejectedValueOnce(new NetworkError(new Error('down')))
      .mockResolvedValueOnce([availabilityException({ id: 'b1', reason: 'Summer vacation' })]);
    vi.spyOn(api.availability, 'getOverrides').mockResolvedValue([]);
    const { user } = renderWithProviders(<DateExceptionsPage />, {
      route: '/dashboard/p1/settings/date-exceptions',
      path: '/dashboard/:pageId/settings/date-exceptions',
    });

    await screen.findByRole('alert');
    await user.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByText(/Summer vacation/)).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(getExceptions).toHaveBeenCalledTimes(2);
  });

  it('keeps the exceptions on screen when a reload after a delete fails', async () => {
    // The refresh case, which must not be treated as an initial load: the rows
    // are real, and blanking them would be a worse answer than a stale one.
    const kept = availabilityException({ id: 'b2', reason: 'Conference' });
    vi.spyOn(api.availability, 'getExceptions')
      .mockResolvedValueOnce([kept, availabilityException({ id: 'b1', date: '2026-09-01', reason: 'Summer vacation' })])
      .mockRejectedValue(new NetworkError(new Error('down')));
    vi.spyOn(api.availability, 'getOverrides').mockResolvedValue([]);
    vi.spyOn(api.availability, 'deleteException').mockResolvedValue(undefined);

    const { user } = renderWithProviders(<DateExceptionsPage />, {
      route: '/dashboard/p1/settings/date-exceptions',
      path: '/dashboard/:pageId/settings/date-exceptions',
    });
    await screen.findByText(/Conference/);

    await user.click(screen.getAllByRole('button', { name: /^Delete blocked period/ })[0]);
    await user.click(screen.getByRole('button', { name: 'Delete blocked period' }));

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.getByText(/Conference/)).toBeInTheDocument();
  });
});
