import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { StepProgress } from '../StepProgress';
import { renderWithProviders as render } from '../../../test/render';

/**
 * Where am I, what is done, what is next - and nothing else.
 *
 * The indicator this replaced drew six numbered nodes with connector lines at
 * the top of every screen, hid its own labels below `sm`, and listed
 * "Confirmation" as a step still to be reached.
 */
describe('StepProgress', () => {
  it('marks the current step and nothing else', () => {
    render(<StepProgress currentStep="details" onGoToStep={vi.fn()} />);

    const current = screen.getByText('Details');
    expect(current).toHaveAttribute('aria-current', 'step');
    expect(screen.getByText('Confirm')).not.toHaveAttribute('aria-current');
  });

  it('makes a completed step a real control, because going back is the point', async () => {
    const onGoToStep = vi.fn();
    const { user } = render(<StepProgress currentStep="confirm" onGoToStep={onGoToStep} />);

    await user.click(screen.getByRole('button', { name: /^Time/ }));

    expect(onGoToStep).toHaveBeenCalledWith('time');
  });

  it('offers no control for a step that is not reachable yet', () => {
    render(<StepProgress currentStep="time" onGoToStep={vi.fn()} />);

    expect(screen.queryByRole('button', { name: /Confirm/ })).not.toBeInTheDocument();
    expect(screen.getByText('Confirm')).toHaveAttribute('aria-disabled', 'true');
  });

  it('shows three steps, all of them named at every size', () => {
    render(<StepProgress currentStep="time" onGoToStep={vi.fn()} />);

    const nav = screen.getByRole('navigation', { name: /booking steps/i });
    expect(nav).toHaveTextContent('Time');
    expect(nav).toHaveTextContent('Details');
    expect(nav).toHaveTextContent('Confirm');
  });

  it('does not list the confirmation as somewhere still to go', () => {
    render(<StepProgress currentStep="confirm" onGoToStep={vi.fn()} />);

    expect(screen.getByRole('navigation', { name: /booking steps/i })).not.toHaveTextContent(/confirmation/i);
  });
});
