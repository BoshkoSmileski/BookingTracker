import { Check } from 'lucide-react';
import { FOCUS_RING } from '../ui';

/**
 * `success` is deliberately not one of the numbered steps: it is the outcome of
 * finishing them, not somewhere the guest is on their way to. The old indicator
 * listed "Confirmation" as a sixth node, which quietly promised there was still
 * something to do after confirming.
 */
export type WizardStep = 'time' | 'details' | 'confirm' | 'success';

const WIZARD_STEPS: { key: Exclude<WizardStep, 'success'>; label: string }[] = [
  { key: 'time', label: 'Time' },
  { key: 'details', label: 'Details' },
  { key: 'confirm', label: 'Confirm' },
];

function stepIndex(step: WizardStep): number {
  return WIZARD_STEPS.findIndex((s) => s.key === step);
}

interface StepProgressProps {
  currentStep: WizardStep;
  /** Jump back to an already-completed step. Never offered for a step ahead. */
  onGoToStep: (step: Exclude<WizardStep, 'success'>) => void;
}

/**
 * Where the guest is, in three words.
 *
 * This replaced a six-node numbered railway with connector lines that sat at
 * the very top of every screen - the loudest element on the page, describing
 * navigation rather than content, and collapsing to six anonymous dots below
 * `sm` because the labels were `hidden sm:block`. Six steps was itself the
 * problem being drawn: two of them (pick a service, pick a date) asked for
 * something the guest could not meaningfully answer separately.
 *
 * What is left says the same three things a progress indicator is for - where
 * am I, what is done, what is next - at meta size, in the flow of the page:
 *
 *   - A **completed** step is a real button. Going back to change an answer is
 *     the single most common thing a guest wants from a progress indicator, and
 *     drawing one that looks like navigation and is not is worse than drawing
 *     nothing.
 *   - The **current** step carries `aria-current="step"` and the accent.
 *   - A **future** step is plain text with `aria-disabled`, because it is not
 *     reachable yet and must not be announced as a control.
 *
 * All three labels fit at 320px, so nothing is hidden on a phone - the mobile
 * simplification is that there are three of them rather than six.
 */
export function StepProgress({ currentStep, onGoToStep }: StepProgressProps) {
  const currentIndex = stepIndex(currentStep);

  return (
    <nav aria-label="Booking steps" className="mt-6 border-t border-gray-200 pt-4">
      <ol className="flex flex-wrap items-center gap-x-1 gap-y-1 text-[13px]">
        {WIZARD_STEPS.map((step, index) => {
          const isComplete = index < currentIndex;
          const isCurrent = index === currentIndex;

          return (
            <li key={step.key} className="flex items-center">
              {index > 0 && (
                <span aria-hidden="true" className="px-2 text-gray-300">
                  /
                </span>
              )}
              {isComplete ? (
                <button
                  type="button"
                  onClick={() => onGoToStep(step.key)}
                  aria-label={`${step.label} — completed, go back to change it`}
                  className={`inline-flex items-center gap-1.5 rounded px-1 py-0.5 font-medium text-accent-700 transition-colors hover:text-accent-900 hover:underline ${FOCUS_RING}`}
                >
                  <Check className="h-3.5 w-3.5" aria-hidden="true" />
                  {step.label}
                </button>
              ) : (
                <span
                  aria-current={isCurrent ? 'step' : undefined}
                  aria-disabled={isCurrent ? undefined : true}
                  className={`px-1 py-0.5 ${isCurrent ? 'font-semibold text-gray-900' : 'text-gray-500'}`}
                >
                  {step.label}
                </span>
              )}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}
