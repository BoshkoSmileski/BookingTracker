import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LoadError } from '../ui';
import { renderWithProviders } from '../../test/render';

/**
 * The shape every screen now reports a failed load in.
 *
 * The pages each prove their own wiring; this pins the contract they all rely
 * on, so a change to the treatment is a change to one test rather than to
 * fourteen. The two things that matter are that it is announced (an unread
 * error is the bug this whole pass exists to fix, one layer up) and that the
 * retry is a real control rather than a sentence telling someone to reload.
 */
describe('LoadError', () => {
  it('announces itself, so a failure is not something you have to be looking at', () => {
    renderWithProviders(<LoadError message="Could not load your working hours." />);

    // role=alert comes from InlineNotice's error tone - the same treatment every
    // other error on the screen gets, rather than a second one for loads.
    expect(screen.getByRole('alert')).toHaveTextContent('Could not load your working hours.');
  });

  it('offers Try again as a button when a retry is possible', async () => {
    const onRetry = vi.fn();
    const { user } = renderWithProviders(
      <LoadError message="Could not load these sessions." onRetry={onRetry} />,
    );

    await user.click(screen.getByRole('button', { name: 'Try again' }));
    expect(onRetry).toHaveBeenCalledTimes(1);
  });

  it('omits Try again where retrying cannot help', () => {
    // A mistyped public token fails identically every time, so the control is
    // optional rather than always present.
    renderWithProviders(<LoadError message="This booking could not be loaded." />);

    expect(screen.queryByRole('button', { name: 'Try again' })).not.toBeInTheDocument();
  });
});
