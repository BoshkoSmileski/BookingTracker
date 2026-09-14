import { act, renderHook, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { COPIED_LABEL, useCopyToClipboard } from '../useCopyToClipboard';

/**
 * The shared clipboard echo.
 *
 * Four screens had their own copy of this and only one of them was correct, so
 * what is pinned here is specifically the three things the other three got
 * wrong: a refused clipboard must not claim success, the echo must clear itself,
 * and the timer must not outlive the component.
 *
 * jsdom ships no `navigator.clipboard`, and the component tests only have one
 * because `userEvent.setup()` installs a stub during render. A bare
 * `renderHook` has no such setup, so this file defines the property itself -
 * `configurable`, and deleted afterwards, so nothing leaks into another file.
 */
describe('useCopyToClipboard', () => {
  let writeText: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', {
      value: { writeText },
      configurable: true,
    });
  });

  afterEach(() => {
    vi.useRealTimers();
    Reflect.deleteProperty(navigator, 'clipboard');
  });

  it('remembers the exact text it copied', async () => {
    const { result } = renderHook(() => useCopyToClipboard());

    await act(async () => {
      await result.current.copy('https://example.test/book/demo');
    });

    expect(writeText).toHaveBeenCalledWith('https://example.test/book/demo');
    // The value, not a boolean - which is what lets one hook serve a grid of
    // cards that each copy a different URL.
    expect(result.current.copied).toBe('https://example.test/book/demo');
  });

  it('clears the echo on its own', async () => {
    const { result } = renderHook(() => useCopyToClipboard());

    await act(async () => {
      await result.current.copy('x');
    });
    expect(result.current.copied).toBe('x');

    act(() => {
      vi.advanceTimersByTime(2000);
    });

    await waitFor(() => expect(result.current.copied).toBeNull());
  });

  it('claims nothing when the clipboard refuses', async () => {
    // REGRESSION: three of the four call sites used
    // `void navigator.clipboard.writeText(...)` and then set copied
    // unconditionally, so a refused clipboard - which is what happens outside a
    // secure context - showed "Copied!" for a copy that never happened.
    writeText.mockRejectedValue(new Error('denied'));
    const { result } = renderHook(() => useCopyToClipboard());

    await act(async () => {
      await result.current.copy('x');
    });

    expect(result.current.copied).toBeNull();
  });

  it('does not leave a timer running after unmount', async () => {
    const clearTimeoutSpy = vi.spyOn(window, 'clearTimeout');
    const { result, unmount } = renderHook(() => useCopyToClipboard());

    await act(async () => {
      await result.current.copy('x');
    });
    unmount();

    // REGRESSION: the three hand-rolled versions called setTimeout inside the
    // click handler with nothing clearing it, so copying and then navigating
    // within two seconds left a timer to fire against an unmounted component.
    expect(clearTimeoutSpy).toHaveBeenCalled();
  });

  it('exports one label, so the call sites cannot drift again', () => {
    expect(COPIED_LABEL).toBe('Copied!');
  });
});
