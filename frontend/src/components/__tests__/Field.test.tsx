import { screen } from '@testing-library/react';
import { useState } from 'react';
import { describe, expect, it } from 'vitest';
import { Field, INPUT } from '../ui';
import { renderWithProviders } from '../../test/render';

/**
 * The wiring every form field in this app used to hand-write.
 *
 * `FieldLabel` already pinned the label and the "(optional)" marker; what was
 * still per-site was the part nobody can see - which element the label points
 * at, which paragraph describes the control, and whether the control is marked
 * invalid. That was correct on `WorkingHoursPage`, where somebody had thought
 * about it, and absent nearly everywhere else, so these tests are deliberately
 * about the DOM relationships rather than about the markup expressing them.
 *
 * Ids are never asserted as literals: they come from `useId` and are read back
 * off the elements. That is both how the existing `WorkingHoursPage` test was
 * already written and the only way an id scheme stays an implementation detail.
 */

/** A field in the shape callers actually write one, so the render prop is exercised as used. */
function TextField({
  label,
  hint,
  error,
  optional,
}: {
  label: string;
  hint?: string;
  error?: string;
  optional?: boolean;
}) {
  return (
    <Field label={label} hint={hint} error={error} optional={optional}>
      {(control) => <input {...control} className={INPUT} defaultValue="" />}
    </Field>
  );
}

describe('Field', () => {
  describe('label and control', () => {
    it('names the control, so the label is what finds it', () => {
      renderWithProviders(<TextField label="Time zone" />);

      expect(screen.getByLabelText('Time zone').tagName).toBe('INPUT');
    });

    it('puts the id on the element the caller chose, not on a wrapper', () => {
      // The case that rules out cloneElement: several controls in this app are
      // an input inside a flex row with a unit beside it, and a clone would
      // label the div.
      renderWithProviders(
        <Field label="Default reminder">
          {(control) => (
            <div className="flex items-center gap-2">
              <input {...control} type="number" />
              <span>minutes before</span>
            </div>
          )}
        </Field>,
      );

      expect(screen.getByLabelText('Default reminder').tagName).toBe('INPUT');
    });

    it('marks a field optional in the one way this app says it', () => {
      renderWithProviders(<TextField label="Reason" optional />);

      // A real space between the two, or the accessible name reads
      // "Reason(optional)" - which is why FieldLabel renders one.
      expect(screen.getByLabelText('Reason (optional)')).toBeInTheDocument();
      expect(screen.queryByLabelText('Reason (Optional)')).not.toBeInTheDocument();
    });

    it('says nothing about optionality unless asked', () => {
      renderWithProviders(<TextField label="Title" />);

      expect(screen.getByLabelText('Title')).toBeInTheDocument();
      expect(screen.queryByText(/optional/i)).not.toBeInTheDocument();
    });
  });

  describe('hint', () => {
    it('describes the control with the hint', () => {
      renderWithProviders(
        <TextField label="Time zone" hint="An IANA identifier, e.g. Europe/Skopje." />,
      );

      const input = screen.getByLabelText('Time zone');
      const hint = screen.getByText('An IANA identifier, e.g. Europe/Skopje.');
      expect(hint.id).not.toBe('');
      expect(input).toHaveAttribute('aria-describedby', hint.id);
    });

    it('is not an error, so nothing is marked invalid', () => {
      renderWithProviders(<TextField label="Time zone" hint="An IANA identifier." />);

      expect(screen.getByLabelText('Time zone')).not.toHaveAttribute('aria-invalid');
    });
  });

  describe('error', () => {
    it('describes the control with the error and marks it invalid', () => {
      renderWithProviders(
        <TextField
          label="Time zone"
          error="'Europe/Skopj' is not a recognized IANA time zone id."
        />,
      );

      const input = screen.getByLabelText('Time zone');
      const message = screen.getByText(/is not a recognized IANA time zone id/);
      expect(input).toHaveAttribute('aria-describedby', message.id);
      expect(input).toHaveAttribute('aria-invalid', 'true');
    });

    it('treats an empty string as no error at all', () => {
      // `validationErrors(e).for(key)[0]` is undefined for a field the server
      // did not complain about, and a caller may just as easily pass ''. Both
      // have to mean "fine", or every untouched field renders as invalid.
      renderWithProviders(<TextField label="Time zone" error="" hint="An IANA identifier." />);

      const input = screen.getByLabelText('Time zone');
      expect(input).not.toHaveAttribute('aria-invalid');
      expect(input).toHaveAttribute('aria-describedby', screen.getByText('An IANA identifier.').id);
    });
  });

  describe('an error takes the place of the hint', () => {
    it('shows the error instead of the hint, not as well as it', () => {
      // WorkingHoursPage established this: "An IANA identifier, e.g.
      // Europe/Skopje" underneath "'Europe/Skopj' is not a recognized IANA time
      // zone id." is the same sentence twice.
      renderWithProviders(
        <TextField
          label="Time zone"
          hint="An IANA identifier, e.g. Europe/Skopje."
          error="Time zone is not recognized."
        />,
      );

      expect(screen.getByText('Time zone is not recognized.')).toBeInTheDocument();
      expect(screen.queryByText('An IANA identifier, e.g. Europe/Skopje.')).not.toBeInTheDocument();
    });

    it('points aria-describedby at the error, and only the error', () => {
      renderWithProviders(
        <TextField label="Time zone" hint="An IANA identifier." error="Not recognized." />,
      );

      const describedBy = screen.getByLabelText('Time zone').getAttribute('aria-describedby');
      expect(describedBy).toBe(screen.getByText('Not recognized.').id);
      // One id, not a list that still carries the hint's.
      expect(describedBy?.split(' ')).toHaveLength(1);
    });
  });

  describe('neither hint nor error', () => {
    it('leaves aria-describedby off entirely rather than empty', () => {
      // An IDREF pointing at nothing is a broken reference; some screen readers
      // announce the failure rather than ignoring it.
      renderWithProviders(<TextField label="Max bookings per day" />);

      const input = screen.getByLabelText('Max bookings per day');
      expect(input).not.toHaveAttribute('aria-describedby');
      expect(input).not.toHaveAttribute('aria-invalid');
    });
  });

  describe('ids', () => {
    it('gives two fields with the same shape different ids', () => {
      // The hand-written ids this replaced were unique only by inspection.
      renderWithProviders(
        <>
          <TextField label="Start" hint="When it opens." />
          <TextField label="End" hint="When it closes." />
        </>,
      );

      const start = screen.getByLabelText('Start');
      const end = screen.getByLabelText('End');
      expect(start.id).not.toBe(end.id);
      expect(start.getAttribute('aria-describedby')).toBe(screen.getByText('When it opens.').id);
      expect(end.getAttribute('aria-describedby')).toBe(screen.getByText('When it closes.').id);
    });

    it('keeps the control id stable across a rerender, so the label never comes loose', async () => {
      function Editable() {
        const [value, setValue] = useState('');
        return (
          <Field label="Time zone" hint="An IANA identifier.">
            {(control) => (
              <input {...control} value={value} onChange={(e) => setValue(e.target.value)} />
            )}
          </Field>
        );
      }
      const { user } = renderWithProviders(<Editable />);

      const before = screen.getByLabelText('Time zone').id;
      await user.type(screen.getByLabelText('Time zone'), 'Europe/Skopje');

      const input = screen.getByLabelText('Time zone');
      expect(input).toHaveValue('Europe/Skopje');
      expect(input.id).toBe(before);
    });

    it('re-points the description when a hint becomes an error', async () => {
      function Rejectable() {
        const [rejected, setRejected] = useState(false);
        return (
          <>
            <Field
              label="Time zone"
              hint="An IANA identifier."
              error={rejected ? 'Not recognized.' : undefined}
            >
              {(control) => <input {...control} defaultValue="" />}
            </Field>
            <button type="button" onClick={() => setRejected(true)}>Save</button>
          </>
        );
      }
      const { user } = renderWithProviders(<Rejectable />);

      const input = screen.getByLabelText('Time zone');
      expect(input).toHaveAttribute('aria-describedby', screen.getByText('An IANA identifier.').id);

      await user.click(screen.getByRole('button', { name: 'Save' }));

      expect(input).toHaveAttribute('aria-describedby', screen.getByText('Not recognized.').id);
      expect(input).toHaveAttribute('aria-invalid', 'true');
      // Same control throughout - a save that swapped the input would lose focus
      // and whatever was typed.
      expect(screen.getByLabelText('Time zone')).toBe(input);
    });
  });
});
