import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useIdleTimer } from '../useIdleTimer';

describe('useIdleTimer', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('fires onIdle once the timeout elapses with no activity', () => {
    const onIdle = vi.fn();
    renderHook(() => useIdleTimer(onIdle, vi.fn(), 1000, true));

    expect(onIdle).not.toHaveBeenCalled();
    act(() => { vi.advanceTimersByTime(1000); });

    expect(onIdle).toHaveBeenCalledTimes(1);
  });

  it('does not fire when disabled', () => {
    // Disabled for non-Active sessions - a submitted booking must not keep
    // recording idle transitions.
    const onIdle = vi.fn();
    renderHook(() => useIdleTimer(onIdle, vi.fn(), 1000, false));

    act(() => { vi.advanceTimersByTime(5000); });

    expect(onIdle).not.toHaveBeenCalled();
  });

  it('restarts the countdown on activity, so a busy user never goes idle', () => {
    const onIdle = vi.fn();
    renderHook(() => useIdleTimer(onIdle, vi.fn(), 1000, true));

    act(() => { vi.advanceTimersByTime(800); });
    act(() => { window.dispatchEvent(new Event('keydown')); });
    act(() => { vi.advanceTimersByTime(800); });

    expect(onIdle).not.toHaveBeenCalled();

    act(() => { vi.advanceTimersByTime(300); });
    expect(onIdle).toHaveBeenCalledTimes(1);
  });

  it('fires onActive only after having gone idle', () => {
    const onIdle = vi.fn();
    const onActive = vi.fn();
    renderHook(() => useIdleTimer(onIdle, onActive, 1000, true));

    // Activity while still active is not a transition and must not emit an event.
    act(() => { window.dispatchEvent(new Event('mousemove')); });
    expect(onActive).not.toHaveBeenCalled();

    act(() => { vi.advanceTimersByTime(1000); });
    expect(onIdle).toHaveBeenCalledTimes(1);

    act(() => { window.dispatchEvent(new Event('mousemove')); });
    expect(onActive).toHaveBeenCalledTimes(1);
  });

  it('emits one idle event per idle period, not one per elapsed interval', () => {
    const onIdle = vi.fn();
    renderHook(() => useIdleTimer(onIdle, vi.fn(), 1000, true));

    act(() => { vi.advanceTimersByTime(5000); });

    expect(onIdle).toHaveBeenCalledTimes(1);
  });

  it('removes its listeners and timer on unmount', () => {
    // CLEANUP: a leaked listener would keep firing tracking events after the
    // wizard unmounts.
    const onIdle = vi.fn();
    const removeSpy = vi.spyOn(window, 'removeEventListener');
    const { unmount } = renderHook(() => useIdleTimer(onIdle, vi.fn(), 1000, true));

    unmount();
    act(() => { vi.advanceTimersByTime(5000); });

    expect(onIdle).not.toHaveBeenCalled();
    expect(removeSpy).toHaveBeenCalled();
  });

  it('stops firing when it flips from enabled to disabled', () => {
    const onIdle = vi.fn();
    const { rerender } = renderHook(
      ({ enabled }) => useIdleTimer(onIdle, vi.fn(), 1000, enabled),
      { initialProps: { enabled: true } },
    );

    rerender({ enabled: false });
    act(() => { vi.advanceTimersByTime(5000); });

    expect(onIdle).not.toHaveBeenCalled();
  });
});
