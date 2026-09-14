import { screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { WorkingHoursPage } from '../WorkingHoursPage';
import { ApiError, NetworkError, api } from '../../lib/api';
import { renderWithProviders } from '../../test/render';
import { resetAuthState } from '../../test/authContextMock';
import type { WorkingDayDto, WorkingScheduleDto } from '../../lib/types';

vi.mock('../../contexts/AuthContext', () => import('../../test/authContextMock'));

/**
 * The screen a new organizer is sent to before anything can be booked.
 *
 * Two things are pinned here. A day that is switched on with no intervals saves
 * happily and then produces no slots at all, which looked exactly like a day
 * that was working - the row simply showed an "Add interval" button where the
 * times would be. And a rejected save used to report the two words "Validation
 * failed", discarding the only sentence that explains it.
 */

const PAGE_ID = '11111111-1111-1111-1111-111111111111';

function schedule(days: WorkingDayDto[]): WorkingScheduleDto {
  return { id: 's', organizerId: 'o', timeZoneId: 'Europe/Skopje', days };
}

const MONDAY_9_TO_5: WorkingDayDto = {
  dayOfWeek: 1,
  isEnabled: true,
  intervals: [{ start: '09:00:00', end: '17:00:00' }],
};

function renderPage() {
  return renderWithProviders(<WorkingHoursPage />, {
    route: `/dashboard/${PAGE_ID}/settings/hours`,
    path: '/dashboard/:pageId/settings/hours',
  });
}

/** The row for a weekday, which is where a per-day warning has to appear. */
const dayRow = (name: string) => screen.getByRole('checkbox', { name }).closest('li') as HTMLElement;

beforeEach(() => {
  resetAuthState();
  vi.spyOn(api.availability, 'saveSchedule').mockResolvedValue(schedule([MONDAY_9_TO_5]));
});

describe('WorkingHoursPage — a day switched on with no hours', () => {
  it('says so on the row itself, where a closed day says "Closed"', async () => {
    vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(
      schedule([{ dayOfWeek: 1, isEnabled: true, intervals: [] }]),
    );
    renderPage();

    const monday = await screen.findByRole('checkbox', { name: 'Monday' });
    expect(within(monday.closest('li')!).getByText(/no hours set/i)).toBeInTheDocument();
  });

  it('warns once for the whole form, naming the day', async () => {
    vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(
      schedule([{ dayOfWeek: 1, isEnabled: true, intervals: [] }]),
    );
    renderPage();

    const notice = await screen.findByText(/switched on but has no hours/i);
    expect(notice).toHaveTextContent('Monday');
    expect(notice).toHaveTextContent(/guests cannot book it/i);
  });

  it('names every affected day, not just the first', async () => {
    vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(
      schedule([
        { dayOfWeek: 1, isEnabled: true, intervals: [] },
        { dayOfWeek: 4, isEnabled: true, intervals: [] },
      ]),
    );
    renderPage();

    const notice = await screen.findByText(/switched on but have no hours/i);
    expect(notice).toHaveTextContent('Monday');
    expect(notice).toHaveTextContent('Thursday');
  });

  it('says nothing when every enabled day has hours', async () => {
    vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(schedule([MONDAY_9_TO_5]));
    renderPage();

    await screen.findByRole('checkbox', { name: 'Monday' });
    expect(screen.queryByText(/no hours set/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/switched on but/i)).not.toBeInTheDocument();
  });

  it('says nothing about a day that is simply closed', async () => {
    // A disabled day with no intervals is the normal state of most of the week
    // and is not a mistake.
    vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(
      schedule([{ dayOfWeek: 1, isEnabled: false, intervals: [] }]),
    );
    renderPage();

    const monday = await screen.findByRole('checkbox', { name: 'Monday' });
    expect(within(monday.closest('li')!).getByText('Closed')).toBeInTheDocument();
    expect(screen.queryByText(/switched on but/i)).not.toBeInTheDocument();
  });

  it('appears as soon as a day is switched on, before anything is saved', async () => {
    vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(schedule([]));
    const { user } = renderPage();

    await user.click(await screen.findByRole('checkbox', { name: 'Wednesday' }));

    expect(await screen.findByText(/switched on but has no hours/i)).toHaveTextContent('Wednesday');
    expect(within(dayRow('Wednesday')).getByText(/no hours set/i)).toBeInTheDocument();
  });
});

describe('WorkingHoursPage — a successful save', () => {
  /**
   * This screen used to `navigate` away to the booking page's session list on
   * success - the only settings screen in the app that did - so the sole
   * confirmation that a whole week had saved was the screen vanishing, onto a
   * destination that says nothing about the schedule. A successful save and a
   * silent no-op looked identical.
   */
  beforeEach(() => {
    vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(schedule([MONDAY_9_TO_5]));
  });

  it('stays on the screen and says it saved', async () => {
    const { user } = renderPage();

    await user.click(await screen.findByRole('button', { name: /save schedule/i }));

    expect(await screen.findByText('Saved.')).toBeInTheDocument();
    // Still editable, which is the point: the common case is another pass.
    expect(screen.getByRole('checkbox', { name: 'Monday' })).toBeInTheDocument();
  });

  it('re-seeds the form from what the server actually stored', async () => {
    // The server orders and normalises the week, so the screen should show what
    // a reload would rather than what was typed.
    vi.spyOn(api.availability, 'saveSchedule').mockResolvedValue(
      schedule([{ dayOfWeek: 3, isEnabled: true, intervals: [{ start: '11:00:00', end: '15:00:00' }] }]),
    );
    const { user } = renderPage();

    await user.click(await screen.findByRole('button', { name: /save schedule/i }));

    await screen.findByText('Saved.');
    expect(screen.getByRole('checkbox', { name: 'Wednesday' })).toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Monday' })).not.toBeChecked();
  });

  it('clears the confirmation when the next save fails', async () => {
    const save = vi.spyOn(api.availability, 'saveSchedule').mockResolvedValue(schedule([MONDAY_9_TO_5]));
    const { user } = renderPage();

    await user.click(await screen.findByRole('button', { name: /save schedule/i }));
    await screen.findByText('Saved.');

    save.mockRejectedValue(new NetworkError());
    await user.click(screen.getByRole('button', { name: /save schedule/i }));

    expect(await screen.findByText(/could not reach the server/i)).toBeInTheDocument();
    expect(screen.queryByText('Saved.')).not.toBeInTheDocument();
  });
});

describe('WorkingHoursPage — copy Monday to weekdays', () => {
  const copyButton = () => screen.getByRole('button', { name: /copy monday to weekdays/i });

  /** The times shown on a row, as "HH:mm-HH:mm" pairs. */
  const hoursOf = (name: string) =>
    within(dayRow(name))
      .queryAllByLabelText(/interval \d+ (start|end)/i)
      .map((input) => (input as HTMLInputElement).value);

  it('applies Monday’s hours to Tuesday through Friday', async () => {
    vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(schedule([MONDAY_9_TO_5]));
    const { user } = renderPage();

    await user.click(await screen.findByRole('button', { name: /copy monday to weekdays/i }));

    for (const day of ['Tuesday', 'Wednesday', 'Thursday', 'Friday']) {
      expect(screen.getByRole('checkbox', { name: day })).toBeChecked();
      expect(hoursOf(day)).toEqual(['09:00', '17:00']);
    }
  });

  it('leaves the weekend alone', async () => {
    // Weekdays only, deliberately: opening Saturday because Monday is open is
    // the button doing something nobody asked it to.
    vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(
      schedule([MONDAY_9_TO_5, { dayOfWeek: 6, isEnabled: false, intervals: [] }]),
    );
    const { user } = renderPage();

    await user.click(await screen.findByRole('button', { name: /copy monday to weekdays/i }));

    expect(screen.getByRole('checkbox', { name: 'Saturday' })).not.toBeChecked();
    expect(screen.getByRole('checkbox', { name: 'Sunday' })).not.toBeChecked();
    expect(within(dayRow('Saturday')).getByText('Closed')).toBeInTheDocument();
  });

  it('leaves Monday itself untouched', async () => {
    vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(schedule([MONDAY_9_TO_5]));
    const { user } = renderPage();

    await user.click(await screen.findByRole('button', { name: /copy monday to weekdays/i }));

    expect(hoursOf('Monday')).toEqual(['09:00', '17:00']);
  });

  it('is disabled until Monday has hours to copy', async () => {
    vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(
      schedule([{ dayOfWeek: 1, isEnabled: true, intervals: [] }]),
    );
    renderPage();

    await screen.findByRole('checkbox', { name: 'Monday' });
    expect(copyButton()).toBeDisabled();
  });

  it('becomes available as soon as Monday has an interval', async () => {
    vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(
      schedule([{ dayOfWeek: 1, isEnabled: true, intervals: [] }]),
    );
    const { user } = renderPage();

    await screen.findByRole('checkbox', { name: 'Monday' });
    await user.click(within(dayRow('Monday')).getByRole('button', { name: /add interval/i }));

    expect(copyButton()).toBeEnabled();
  });

  it('saves what it copied', async () => {
    const saveSchedule = vi.spyOn(api.availability, 'saveSchedule').mockResolvedValue(schedule([MONDAY_9_TO_5]));
    vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(schedule([MONDAY_9_TO_5]));
    const { user } = renderPage();

    await user.click(await screen.findByRole('button', { name: /copy monday to weekdays/i }));
    await user.click(screen.getByRole('button', { name: /save schedule/i }));

    const days = saveSchedule.mock.calls[0][2];
    expect(days.filter((d) => d.isEnabled).map((d) => d.dayOfWeek).sort()).toEqual([1, 2, 3, 4, 5]);
  });

  it('does not disturb a day the organizer edited on its own', async () => {
    // Editing one day must never modify another - the only thing that moves
    // hours across days is this button.
    vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(
      schedule([MONDAY_9_TO_5, { dayOfWeek: 4, isEnabled: true, intervals: [{ start: '13:00:00', end: '15:00:00' }] }]),
    );
    const { user } = renderPage();

    await screen.findByRole('checkbox', { name: 'Thursday' });
    const start = within(dayRow('Thursday')).getByLabelText('Thursday interval 1 start');
    await user.clear(start);
    await user.type(start, '14:00');

    expect(hoursOf('Monday')).toEqual(['09:00', '17:00']);
    expect(hoursOf('Thursday')).toEqual(['14:00', '15:00']);
  });
});

describe('WorkingHoursPage — a rejected save', () => {
  beforeEach(() => {
    vi.spyOn(api.availability, 'getSchedule').mockResolvedValue(schedule([MONDAY_9_TO_5]));
  });

  const save = async (user: ReturnType<typeof renderWithProviders>['user']) => {
    await user.click(await screen.findByRole('button', { name: /save schedule/i }));
  };

  it('puts the time zone message under the time zone box', async () => {
    // REGRESSION: this rendered as "Validation failed", so the one sentence
    // naming what was wrong with the value just typed never appeared.
    vi.spyOn(api.availability, 'saveSchedule').mockRejectedValue(
      new ApiError(400, {
        title: 'Validation failed',
        errors: { TimeZoneId: ["'Europe/Skopj' is not a recognized IANA time zone id."] },
      }),
    );
    const { user } = renderPage();

    await save(user);

    const field = await screen.findByLabelText('Time zone');
    const message = await screen.findByText(/is not a recognized IANA time zone id/i);
    expect(field).toHaveAttribute('aria-invalid', 'true');
    expect(field).toHaveAttribute('aria-describedby', message.id);
    expect(screen.queryByText(/^Validation failed$/)).not.toBeInTheDocument();
  });

  it('does not repeat a field message in the form-level notice', async () => {
    vi.spyOn(api.availability, 'saveSchedule').mockRejectedValue(
      new ApiError(400, {
        title: 'Validation failed',
        errors: { TimeZoneId: ['Time zone is not recognized.'] },
      }),
    );
    const { user } = renderPage();

    await save(user);

    await screen.findByText('Time zone is not recognized.');
    expect(screen.getAllByText('Time zone is not recognized.')).toHaveLength(1);
  });

  it('shows a message no field claimed at form level', async () => {
    // Per-interval keys name a day by .NET's Sunday-zero index, which is
    // deliberately not mapped to a row - so the message must still be shown.
    vi.spyOn(api.availability, 'saveSchedule').mockRejectedValue(
      new ApiError(400, {
        title: 'Validation failed',
        errors: { 'Days[3].Intervals': ['Intervals within a single day must not overlap.'] },
      }),
    );
    const { user } = renderPage();

    await save(user);

    expect(await screen.findByText(/intervals within a single day must not overlap/i)).toBeInTheDocument();
  });

  it('still reports an unreachable server as a connectivity problem', async () => {
    // The ApiError/NetworkError split must survive: a down API is not a
    // rejected schedule.
    vi.spyOn(api.availability, 'saveSchedule').mockRejectedValue(new NetworkError());
    const { user } = renderPage();

    await save(user);

    expect(await screen.findByText(/could not reach the server/i)).toBeInTheDocument();
  });

  it('clears a stale field message on the next attempt', async () => {
    const saveSchedule = vi.spyOn(api.availability, 'saveSchedule').mockRejectedValue(
      new ApiError(400, { title: 'Validation failed', errors: { TimeZoneId: ['Bad zone.'] } }),
    );
    const { user } = renderPage();

    await save(user);
    await screen.findByText('Bad zone.');

    saveSchedule.mockResolvedValue(schedule([MONDAY_9_TO_5]));
    await save(user);

    await waitFor(() => expect(screen.queryByText('Bad zone.')).not.toBeInTheDocument());
  });
});

/**
 * The full initial-load contract, on the screen where getting it wrong was
 * worst: `emptyWeek()` is seven closed days, so had the failed request resolved
 * into the editor instead of leaving the skeleton up, the organizer would have
 * been shown a blank week - and saving it would have closed a schedule they
 * never touched.
 */
describe('WorkingHoursPage — the schedule could not be loaded', () => {
  it('shows the skeleton only while the request is actually pending', async () => {
    let resolve: (value: WorkingScheduleDto) => void = () => {};
    vi.spyOn(api.availability, 'getSchedule').mockReturnValue(
      new Promise<WorkingScheduleDto>((r) => { resolve = r; }),
    );
    renderPage();

    expect(screen.getByRole('status', { name: 'Loading' })).toBeInTheDocument();

    resolve(schedule([MONDAY_9_TO_5]));
    await waitFor(() => expect(screen.queryByRole('status', { name: 'Loading' })).not.toBeInTheDocument());
  });

  it('leaves the skeleton and says so instead of loading for ever', async () => {
    vi.spyOn(api.availability, 'getSchedule').mockRejectedValue(new NetworkError(new Error('down')));
    renderPage();

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.queryByRole('status', { name: 'Loading' })).not.toBeInTheDocument();
  });

  it('never renders the editor over a load that failed', async () => {
    vi.spyOn(api.availability, 'getSchedule').mockRejectedValue(new NetworkError(new Error('down')));
    renderPage();

    await screen.findByRole('alert');
    // Seven closed days would be a claim about this organizer's week.
    expect(screen.queryByRole('checkbox', { name: 'Monday' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /save schedule/i })).not.toBeInTheDocument();
  });

  it('reports an unreachable API as connectivity, not as a rejected request', async () => {
    vi.spyOn(api.availability, 'getSchedule').mockRejectedValue(new NetworkError(new Error('down')));
    renderPage();

    // errorMessage's network wording, not this page's own fallback sentence.
    expect(await screen.findByRole('alert')).not.toHaveTextContent('Could not load your working hours.');
  });

  it('retries on demand and renders the week when the retry succeeds', async () => {
    const getSchedule = vi.spyOn(api.availability, 'getSchedule')
      .mockRejectedValueOnce(new NetworkError(new Error('down')))
      .mockResolvedValueOnce(schedule([MONDAY_9_TO_5]));
    const { user } = renderPage();

    await screen.findByRole('alert');
    await user.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByRole('checkbox', { name: 'Monday' })).toBeChecked();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(getSchedule).toHaveBeenCalledTimes(2);
  });
});
