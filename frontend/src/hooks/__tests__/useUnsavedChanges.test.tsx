import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { Link, MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { LEAVE_LABEL, STAY_LABEL, UNSAVED_CHANGES_TITLE, useUnsavedChanges } from '../useUnsavedChanges';

/**
 * The navigation guard, driven the way a page drives it rather than through
 * `renderHook`.
 *
 * `renderHook` would give a hook with no router, no anchors and no document to
 * listen on - which is three quarters of what this hook is. What it does is
 * only observable through a real `Link` being clicked and a real
 * `beforeunload` listener being asked, so the harness below is a miniature
 * settings page: a field, a Save button that can be made to fail, and a rail
 * link out.
 */

/** Where a blocked link tries to go, and the screen that proves it got there. */
const AWAY = '/dashboard/elsewhere';

function Harness({
  save = async () => {},
  initial = 'loaded value',
  onLinkClick,
}: {
  save?: () => Promise<void>;
  initial?: string;
  /** Stands in for the shell's `onNavigate`, which closes the mobile drawer. */
  onLinkClick?: () => void;
}) {
  const [value, setValue] = useState('');
  const [loaded, setLoaded] = useState(false);
  const { isDirty, markSaved, prompt } = useUnsavedChanges(value);

  // Stands in for the load every one of these pages does: placeholder state
  // first, then server data adopted as the baseline.
  const load = () => {
    setValue(initial);
    setLoaded(true);
    markSaved();
  };

  const onSave = async () => {
    try {
      await save();
      markSaved();
    } catch {
      // Deliberately swallowed, exactly as a page's `saveFailed` branch does:
      // what matters here is that the baseline did NOT move.
    }
  };

  return (
    <div>
      <button type="button" onClick={load}>Load</button>
      <p>{isDirty ? 'dirty' : 'clean'}</p>
      <p>{loaded ? 'loaded' : 'loading'}</p>
      {prompt}
      <label htmlFor="field">Field</label>
      <input id="field" value={value} onChange={(e) => setValue(e.target.value)} />
      <button type="button" onClick={() => void onSave()}>Save</button>
      <Link to={AWAY} onClick={onLinkClick}>Go elsewhere</Link>
      <Link to="/current">Stay here</Link>
      <a href="#main-content">Skip to main content</a>
      <a href="https://example.test/docs">External docs</a>
    </div>
  );
}

function renderHarness(props: Parameters<typeof Harness>[0] = {}) {
  const user = userEvent.setup();
  return {
    user,
    ...render(
      <MemoryRouter initialEntries={['/current']}>
        <Routes>
          <Route path="/current" element={<Harness {...props} />} />
          <Route path={AWAY} element={<p>Elsewhere</p>} />
        </Routes>
      </MemoryRouter>,
    ),
  };
}

/** Load, then type - the state every "dirty" assertion below starts from. */
async function makeDirty(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: 'Load' }));
  await screen.findByText('clean');
  await user.type(screen.getByLabelText('Field'), '!');
  await screen.findByText('dirty');
}

/**
 * `beforeunload` cannot be answered in jsdom - there is no browser dialog and
 * no unload to cancel - so what is asserted is the only thing the page controls:
 * whether the event was cancelled. `dispatchEvent` returns false when a listener
 * called `preventDefault`.
 */
function unloadIsBlocked(): boolean {
  const event = new Event('beforeunload', { cancelable: true });
  return !window.dispatchEvent(event);
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe('useUnsavedChanges — dirty state', () => {
  it('is clean before any data has been adopted, so a placeholder never warns', async () => {
    renderHarness();

    // Nothing has been loaded, so there is no baseline and nothing to lose -
    // the fail-safe default. This is the case a mount-time baseline would get
    // wrong on every one of these screens.
    expect(screen.getByText('clean')).toBeInTheDocument();
    expect(unloadIsBlocked()).toBe(false);
  });

  it('stays clean when the load populates the form', async () => {
    const { user } = renderHarness({ initial: 'from the server' });

    await user.click(screen.getByRole('button', { name: 'Load' }));

    expect(await screen.findByText('clean')).toBeInTheDocument();
    expect(screen.getByLabelText('Field')).toHaveValue('from the server');
  });

  it('becomes dirty once a persisted value is edited', async () => {
    const { user } = renderHarness();
    await makeDirty(user);

    expect(screen.getByText('dirty')).toBeInTheDocument();
  });

  it('goes clean again when the value is put back to what was loaded', async () => {
    const { user } = renderHarness({ initial: 'abc' });
    await user.click(screen.getByRole('button', { name: 'Load' }));
    await screen.findByText('clean');

    await user.type(screen.getByLabelText('Field'), 'd');
    expect(await screen.findByText('dirty')).toBeInTheDocument();

    // Reverting is not a new edit, it is the absence of one.
    await user.keyboard('{Backspace}');
    expect(await screen.findByText('clean')).toBeInTheDocument();
  });

  it('goes clean when a save succeeds', async () => {
    const { user } = renderHarness();
    await makeDirty(user);

    await user.click(screen.getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('clean')).toBeInTheDocument();
  });

  it('stays dirty when a save fails, so the edits are still guarded', async () => {
    const { user } = renderHarness({ save: () => Promise.reject(new Error('nope')) });
    await makeDirty(user);

    await user.click(screen.getByRole('button', { name: 'Save' }));

    // The whole point: a rejected save leaves real unsaved work on screen, and
    // clearing the baseline on the attempt rather than on the outcome would
    // have thrown away the guard exactly when it is needed.
    await waitFor(() => expect(screen.getByText('dirty')).toBeInTheDocument());
    expect(unloadIsBlocked()).toBe(true);
  });
});

describe('useUnsavedChanges — leaving the app', () => {
  it('does not touch unload while the page is clean', async () => {
    const { user } = renderHarness();
    await user.click(screen.getByRole('button', { name: 'Load' }));
    await screen.findByText('clean');

    expect(unloadIsBlocked()).toBe(false);
  });

  it('cancels unload once there are unsaved changes', async () => {
    const { user } = renderHarness();
    await makeDirty(user);

    expect(unloadIsBlocked()).toBe(true);
  });

  it('stops cancelling unload after the changes are saved', async () => {
    const { user } = renderHarness();
    await makeDirty(user);
    expect(unloadIsBlocked()).toBe(true);

    await user.click(screen.getByRole('button', { name: 'Save' }));
    await screen.findByText('clean');

    expect(unloadIsBlocked()).toBe(false);
  });

  it('leaves no listener behind when the page unmounts', async () => {
    const { user, unmount } = renderHarness();
    await makeDirty(user);
    expect(unloadIsBlocked()).toBe(true);

    unmount();

    // A listener that outlived its page would make every later navigation in
    // the tab ask about work that no longer exists.
    expect(unloadIsBlocked()).toBe(false);
  });
});

describe('useUnsavedChanges — in-app navigation', () => {
  it('lets a link through while the page is clean', async () => {
    const { user } = renderHarness();
    await user.click(screen.getByRole('button', { name: 'Load' }));
    await screen.findByText('clean');

    await user.click(screen.getByRole('link', { name: 'Go elsewhere' }));

    expect(await screen.findByText('Elsewhere')).toBeInTheDocument();
  });

  it('holds the navigation and explains what is at stake', async () => {
    const { user } = renderHarness();
    await makeDirty(user);

    await user.click(screen.getByRole('link', { name: 'Go elsewhere' }));

    expect(await screen.findByText(UNSAVED_CHANGES_TITLE)).toBeInTheDocument();
    expect(screen.getByText(/leaving now discards them/i)).toBeInTheDocument();
    // Still here, and the edit is still on screen.
    expect(screen.queryByText('Elsewhere')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Field')).toHaveValue('loaded value!');
  });

  it('Stay cancels the navigation and keeps every edit', async () => {
    const { user } = renderHarness({ initial: 'abc' });
    await makeDirty(user);

    await user.click(screen.getByRole('link', { name: 'Go elsewhere' }));
    await user.click(await screen.findByRole('button', { name: STAY_LABEL }));

    expect(screen.queryByText(UNSAVED_CHANGES_TITLE)).not.toBeInTheDocument();
    expect(screen.queryByText('Elsewhere')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Field')).toHaveValue('abc!');
    expect(screen.getByText('dirty')).toBeInTheDocument();
  });

  it('Leave proceeds with the navigation that was held', async () => {
    const { user } = renderHarness();
    await makeDirty(user);

    await user.click(screen.getByRole('link', { name: 'Go elsewhere' }));
    await user.click(await screen.findByRole('button', { name: LEAVE_LABEL }));

    // Goes where the original click pointed, rather than to some default.
    expect(await screen.findByText('Elsewhere')).toBeInTheDocument();
  });

  it('does not hold the same navigation twice after Leave', async () => {
    const { user } = renderHarness();
    await makeDirty(user);

    await user.click(screen.getByRole('link', { name: 'Go elsewhere' }));
    await user.click(await screen.findByRole('button', { name: LEAVE_LABEL }));
    await screen.findByText('Elsewhere');

    // `navigate` fires no click, so the listener cannot catch its own escape
    // hatch and hold it again - which would be an inescapable page.
    expect(screen.queryByText(UNSAVED_CHANGES_TITLE)).not.toBeInTheDocument();
  });

  it('can hold a second attempt after the first was cancelled', async () => {
    const { user } = renderHarness();
    await makeDirty(user);

    await user.click(screen.getByRole('link', { name: 'Go elsewhere' }));
    await user.click(await screen.findByRole('button', { name: STAY_LABEL }));
    await user.click(screen.getByRole('link', { name: 'Go elsewhere' }));

    // The blocker must not be left spent after one use.
    expect(await screen.findByText(UNSAVED_CHANGES_TITLE)).toBeInTheDocument();
  });

  it('lets a link to the page you are already on through', async () => {
    const { user } = renderHarness();
    await makeDirty(user);

    await user.click(screen.getByRole('link', { name: 'Stay here' }));

    // Nothing is lost by re-entering the same route, so asking would be noise.
    expect(screen.queryByText(UNSAVED_CHANGES_TITLE)).not.toBeInTheDocument();
  });

  it('lets a same-document jump through, so the skip link still works', async () => {
    const { user } = renderHarness();
    await makeDirty(user);

    await user.click(screen.getByRole('link', { name: 'Skip to main content' }));

    // `SkipLink` is on every shell in the app; holding it would break the one
    // control keyboard users reach first.
    expect(screen.queryByText(UNSAVED_CHANGES_TITLE)).not.toBeInTheDocument();
  });

  // jsdom logs "Not implemented: navigation to another Document" here, which is
  // the point: the click was not intercepted and reached the browser.
  it('lets a link out of the app through, where unload is what asks', async () => {
    const { user } = renderHarness();
    await makeDirty(user);

    await user.click(screen.getByRole('link', { name: 'External docs' }));

    expect(screen.queryByText(UNSAVED_CHANGES_TITLE)).not.toBeInTheDocument();
    expect(unloadIsBlocked()).toBe(true);
  });

  /**
   * The contract the organizer shell's mobile drawer depends on.
   *
   * This listener runs in the capture phase at the document, and it used to
   * call `stopPropagation` as well as `preventDefault` - which swallowed the
   * click before any handler on the link itself could see it, including the
   * `onNavigate` every rail link carries to close the drawer. The navigation
   * was held correctly and the drawer stayed open on top of the panel asking
   * about it, so the tap read as a link that did nothing.
   *
   * `preventDefault` alone is what holds the navigation: `Link` calls its own
   * `onClick` first and navigates only `if (!event.defaultPrevented)`. Both
   * halves are asserted together here, because either alone would pass while
   * the pair was broken.
   */
  it("runs a link's own onClick while still holding the navigation", async () => {
    const onLinkClick = vi.fn();
    const { user } = renderHarness({ onLinkClick });
    await makeDirty(user);

    await user.click(screen.getByRole('link', { name: 'Go elsewhere' }));

    expect(onLinkClick).toHaveBeenCalledTimes(1);
    await screen.findByRole('group', { name: UNSAVED_CHANGES_TITLE });
    expect(screen.queryByText('Elsewhere')).not.toBeInTheDocument();
  });

  it('closes the prompt by itself if the changes get saved while it is open', async () => {
    const { user } = renderHarness();
    await makeDirty(user);

    await user.click(screen.getByRole('link', { name: 'Go elsewhere' }));
    await screen.findByText(UNSAVED_CHANGES_TITLE);

    await user.click(screen.getByRole('button', { name: 'Save' }));

    // There is nothing left to warn about, so a question about it would be
    // stale - and answering "Leave" would still be safe but confusing.
    await waitFor(() => expect(screen.queryByText(UNSAVED_CHANGES_TITLE)).not.toBeInTheDocument());
  });
});

describe('useUnsavedChanges — the prompt itself', () => {
  it('names the group and offers both ways out by name', async () => {
    const { user } = renderHarness();
    await makeDirty(user);
    await user.click(screen.getByRole('link', { name: 'Go elsewhere' }));

    const panel = await screen.findByRole('group', { name: UNSAVED_CHANGES_TITLE });
    expect(within(panel).getByRole('button', { name: LEAVE_LABEL })).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: STAY_LABEL })).toBeInTheDocument();
  });

  it('puts the destructive choice first in the DOM, as every other confirmation does', async () => {
    const { user } = renderHarness();
    await makeDirty(user);
    await user.click(screen.getByRole('link', { name: 'Go elsewhere' }));

    const panel = await screen.findByRole('group', { name: UNSAVED_CHANGES_TITLE });
    const [first, second] = within(panel).getAllByRole('button');
    // Confirm first in the DOM, last on screen, so a
    // stray Enter never discards the work.
    expect(first).toHaveAccessibleName(LEAVE_LABEL);
    expect(second).toHaveAccessibleName(STAY_LABEL);
  });

  it('moves focus to itself, so a held navigation is never silent', async () => {
    const { user } = renderHarness();
    await makeDirty(user);

    await user.click(screen.getByRole('link', { name: 'Go elsewhere' }));

    // These forms are long enough for the panel to open off-screen; without
    // this, blocking a link looks exactly like a link that did nothing.
    const panel = await screen.findByRole('group', { name: UNSAVED_CHANGES_TITLE });
    await waitFor(() => expect(panel.parentElement).toHaveFocus());
  });

  it('can be answered from the keyboard alone', async () => {
    const { user } = renderHarness();
    await makeDirty(user);
    await user.click(screen.getByRole('link', { name: 'Go elsewhere' }));
    await screen.findByRole('group', { name: UNSAVED_CHANGES_TITLE });

    // Focus starts on the panel wrapper, so the next two stops are its buttons.
    await user.tab();
    expect(screen.getByRole('button', { name: LEAVE_LABEL })).toHaveFocus();
    await user.tab();
    expect(screen.getByRole('button', { name: STAY_LABEL })).toHaveFocus();

    await user.keyboard('{Enter}');
    expect(screen.queryByText(UNSAVED_CHANGES_TITLE)).not.toBeInTheDocument();
    expect(screen.queryByText('Elsewhere')).not.toBeInTheDocument();
  });
});
