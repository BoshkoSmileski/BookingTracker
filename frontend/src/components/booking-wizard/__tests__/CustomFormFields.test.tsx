import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { DetailsStep } from '../DetailsStep';
import { ReviewStep } from '../ReviewStep';
import type { BookingFormState } from '../../../hooks/useBookingSessionTracker';
import { bookingFormField, bookingPage } from '../../../test/factories';
import { renderWithProviders as render } from '../../../test/render';

/**
 * The visitor-facing half of custom booking questions: that an organizer's
 * fields are actually asked, that a required one blocks Continue, and that the
 * answers are shown back before confirming.
 *
 * The counterpart to BookingInstructions.test.tsx - which asserts the opposite
 * property for instructions, that they carry no input control at all.
 */

const FILLED: BookingFormState = {
  name: 'Jane Doe',
  email: 'jane@example.com',
  phone: '',
  message: '',
  selectedDate: '2026-08-20',
  selectedTime: '09:00',
  answers: {},
};

function renderDetails(overrides: Partial<Parameters<typeof DetailsStep>[0]> = {}) {
  const onAnswerChange = vi.fn();
  const onContinue = vi.fn();
  const result = render(
    <DetailsStep
      formState={FILLED}
      formFields={[]}
      onFieldChange={() => {}}
      onAnswerChange={onAnswerChange}
      onContinue={onContinue}
      onBack={() => {}}
      {...overrides}
    />,
  );
  return { ...result, onAnswerChange, onContinue };
}

describe('DetailsStep custom questions', () => {
  it('asks a short-text question as a single-line input', () => {
    renderDetails({ formFields: [bookingFormField({ label: 'Company', type: 'ShortText' })] });

    const input = screen.getByLabelText(/company/i);
    expect(input.tagName).toBe('INPUT');
  });

  it('asks a long-text question as a textarea', () => {
    renderDetails({
      formFields: [bookingFormField({ label: 'What would you like to discuss?', type: 'LongText' })],
    });

    expect(screen.getByLabelText(/what would you like to discuss/i).tagName).toBe('TEXTAREA');
  });

  it('marks an optional question as optional and leaves a required one unqualified', () => {
    renderDetails({
      formFields: [
        bookingFormField({ id: 'a', label: 'Company', isRequired: true, displayOrder: 0 }),
        bookingFormField({ id: 'b', label: 'Topic', isRequired: false, displayOrder: 1 }),
      ],
    });

    expect(screen.getByLabelText('Company')).toBeRequired();
    expect(screen.getByLabelText('Topic (optional)')).not.toBeRequired();
  });

  it('reports each keystroke as an answer for that field', async () => {
    const { user, onAnswerChange } = renderDetails({
      formFields: [bookingFormField({ id: 'field-1', label: 'Company' })],
    });

    await user.type(screen.getByLabelText(/company/i), 'Ac');

    expect(onAnswerChange).toHaveBeenNthCalledWith(1, 'field-1', 'A');
    expect(onAnswerChange).toHaveBeenNthCalledWith(2, 'field-1', 'c');
  });

  it('shows the answer it was given rather than holding its own state', () => {
    renderDetails({
      formFields: [bookingFormField({ id: 'field-1', label: 'Company' })],
      formState: { ...FILLED, answers: { 'field-1': 'Acme Ltd' } },
    });

    expect(screen.getByLabelText(/company/i)).toHaveValue('Acme Ltd');
  });

  it('blocks Continue while a required question is unanswered, and names it', async () => {
    const { user, onContinue } = renderDetails({
      formFields: [bookingFormField({ id: 'field-1', label: 'Company', isRequired: true })],
    });

    const button = screen.getByRole('button', { name: /continue/i });
    expect(button).toBeDisabled();
    // Names the field that is actually missing - "Name and email are required"
    // would be wrong here, since both are filled in.
    expect(screen.getByText(/still needed: company/i)).toBeInTheDocument();

    await user.click(button);
    expect(onContinue).not.toHaveBeenCalled();
  });

  it('allows Continue once every required question is answered', () => {
    renderDetails({
      formFields: [bookingFormField({ id: 'field-1', label: 'Company', isRequired: true })],
      formState: { ...FILLED, answers: { 'field-1': 'Acme Ltd' } },
    });

    expect(screen.getByRole('button', { name: /continue/i })).toBeEnabled();
  });

  it('does not block Continue for an unanswered optional question', () => {
    renderDetails({ formFields: [bookingFormField({ isRequired: false })] });

    expect(screen.getByRole('button', { name: /continue/i })).toBeEnabled();
  });

  it('leaves the step unchanged for a page that asks nothing', () => {
    renderDetails();

    expect(screen.getByRole('button', { name: /continue/i })).toBeEnabled();
    // Only the four built-in fields.
    expect(screen.getAllByRole('textbox')).toHaveLength(4);
  });
});

describe('ReviewStep custom answers', () => {
  function renderReview(page: ReturnType<typeof bookingPage>, answers: Record<string, string>) {
    return render(
      <ReviewStep
        page={page}
        formState={{ ...FILLED, answers }}
        submitting={false}
        submitError={null}
        submitErrorIsConflict={false}
        onConfirm={() => {}}
        onBack={() => {}}
        onEditTime={() => {}}
        onEditDetails={() => {}}
      />,
    );
  }

  it('shows every answer, labelled, before the visitor confirms', () => {
    const page = bookingPage({
      formFields: [
        bookingFormField({ id: 'a', label: 'Company', displayOrder: 0 }),
        bookingFormField({ id: 'b', label: 'Topic', displayOrder: 1 }),
      ],
    });

    renderReview(page, { a: 'Acme Ltd', b: 'Pricing' });

    expect(screen.getByText('Company')).toBeInTheDocument();
    expect(screen.getByText('Acme Ltd')).toBeInTheDocument();
    expect(screen.getByText('Topic')).toBeInTheDocument();
    expect(screen.getByText('Pricing')).toBeInTheDocument();
  });

  it('omits a question the visitor left blank', () => {
    const page = bookingPage({ formFields: [bookingFormField({ id: 'a', label: 'Company' })] });

    renderReview(page, {});

    expect(screen.queryByText('Company')).not.toBeInTheDocument();
  });
});
