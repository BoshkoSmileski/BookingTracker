import { Info } from 'lucide-react';
import type { BookingInstructionDto } from '../../lib/types';

interface BookingInstructionsProps {
  instructions: BookingInstructionDto[];
  /** Wording above the list. The final step says "before you confirm"; the first says "before you book". */
  heading: string;
  className?: string;
}

/**
 * The organizer's guidance, shown to the visitor.
 *
 * Presented as a labelled notice rather than as plain paragraphs: an amber panel
 * with an icon reads as "information you need", which is what separates this from
 * the service description sitting next to it. Rendering as a list keeps each
 * instruction a discrete item to scan instead of a wall of prose - and there is
 * deliberately no input control anywhere in here, because a visitor never
 * responds to these.
 *
 * Renders nothing when the organizer has not written any, so the wizard is
 * unchanged for the pages that don't use the feature.
 */
export function BookingInstructions({ instructions, heading, className = '' }: BookingInstructionsProps) {
  if (instructions.length === 0) return null;

  return (
    <section aria-label="Booking instructions" className={`rounded-lg border border-amber-200 bg-amber-50 p-4 ${className}`}>
      <h2 className="flex items-center gap-2 text-sm font-semibold text-amber-900">
        <Info className="h-4 w-4 shrink-0" aria-hidden="true" />
        {heading}
      </h2>
      <ul className="mt-2 space-y-1.5">
        {instructions.map((instruction) => (
          <li key={instruction.id} className="flex gap-2 text-sm leading-snug text-amber-900">
            <span aria-hidden="true" className="select-none text-amber-500">&bull;</span>
            <span>{instruction.text}</span>
          </li>
        ))}
      </ul>
    </section>
  );
}
