import { screen, waitFor, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { BookingFormPage } from '../BookingFormPage';
import { api, ApiError, NetworkError } from '../../lib/api';
import { MAX_FORM_FIELDS } from '../../lib/config';
import { bookingFormField, bookingPageDetail } from '../../test/factories';
import { renderWithProviders } from '../../test/render';
import { resetAuthState } from '../../test/authContextMock';

vi.mock('../../contexts/AuthContext', () => import('../../test/authContextMock'));

const PAGE_ID = '11111111-1111-1111-1111-111111111111';

/**
 * The organizer's editor for custom booking questions - the counterpart to
 * BookingInstructionsPage. The behaviour worth pinning is that adding a
 * question sends the type and requiredness the organizer actually chose (not a
 * default), and that the limit is presented as a state of the form rather than
 * only as a server error.
 */
describe('BookingFormPage', () => {
  const renderPage = () =>
    renderWithProviders(<BookingFormPage />, {
      route: `/dashboard/${PAGE_ID}/settings/form`,
      path: '/dashboard/:pageId/settings/form',
    });

  beforeEach(() => {
    resetAuthState();
  });

  it('lists the questions a visitor is asked, with their type and requiredness', async () => {
    vi.spyOn(api.organizer, 'getBookingPage').mockResolvedValue(
      bookingPageDetail({
        formFields: [
          bookingFormField({ id: 'a', label: 'Company', type: 'ShortText', isRequired: true, displayOrder: 0 }),
          bookingFormField({ id: 'b', label: 'Topic', type: 'LongText', isRequired: false, displayOrder: 1 }),
        ],
      }),
    );

    renderPage();

    await screen.findByText('Company');
    // Scoped to the list throughout: "Long text" and "Required" are also the
    // add-form's own option and checkbox label, so an unscoped query would be
    // ambiguous by design rather than by accident.
    const list = screen.getByRole('list');
    expect(within(list).getByText('Company')).toBeInTheDocument();
    expect(within(list).getByText('Topic')).toBeInTheDocument();
    expect(within(list).getByText('Long text')).toBeInTheDocument();
    expect(within(list).getAllByText('Required')).toHaveLength(1);
  });

  it('shows an empty state that says the built-in fields are still asked', async () => {
    vi.spyOn(api.organizer, 'getBookingPage').mockResolvedValue(bookingPageDetail());

    renderPage();

    expect(await screen.findByText(/no questions yet/i)).toBeInTheDocument();
    expect(screen.getByText(/name, email, phone and message/i)).toBeInTheDocument();
  });

  it('adds a question with the type and requiredness that were chosen', async () => {
    vi.spyOn(api.organizer, 'getBookingPage').mockResolvedValue(bookingPageDetail());
    const add = vi.spyOn(api.organizer, 'addBookingFormField').mockResolvedValue(bookingPageDetail());

    const { user } = renderPage();
    await screen.findByText(/no questions yet/i);

    await user.type(screen.getByLabelText(/^question$/i), 'What would you like to discuss?');
    await user.selectOptions(screen.getByLabelText(/answer type/i), 'LongText');
    await user.click(screen.getByLabelText(/required/i));
    await user.click(screen.getByRole('button', { name: /add question/i }));

    await waitFor(() =>
      expect(add).toHaveBeenCalledWith(
        expect.any(String),
        PAGE_ID,
        'What would you like to discuss?',
        'LongText',
        true,
      ),
    );
  });

  it('clears the label but keeps the type and requiredness for the next question', async () => {
    vi.spyOn(api.organizer, 'getBookingPage').mockResolvedValue(bookingPageDetail());
    vi.spyOn(api.organizer, 'addBookingFormField').mockResolvedValue(bookingPageDetail());

    const { user } = renderPage();
    await screen.findByText(/no questions yet/i);

    await user.selectOptions(screen.getByLabelText(/answer type/i), 'LongText');
    await user.type(screen.getByLabelText(/^question$/i), 'Company');
    await user.click(screen.getByRole('button', { name: /add question/i }));

    await waitFor(() => expect(screen.getByLabelText(/^question$/i)).toHaveValue(''));
    expect(screen.getByLabelText(/answer type/i)).toHaveValue('LongText');
  });

  it('removes a question by id', async () => {
    vi.spyOn(api.organizer, 'getBookingPage').mockResolvedValue(
      bookingPageDetail({ formFields: [bookingFormField({ id: 'field-1', label: 'Company' })] }),
    );
    const remove = vi.spyOn(api.organizer, 'removeBookingFormField').mockResolvedValue(bookingPageDetail());

    const { user } = renderPage();
    await screen.findByText('Company');

    await user.click(screen.getByRole('button', { name: /remove question: company/i }));
    // Removing is destructive, so it asks first.
    await user.click(screen.getByRole('button', { name: 'Remove question' }));

    await waitFor(() => expect(remove).toHaveBeenCalledWith(expect.any(String), PAGE_ID, 'field-1'));
  });

  it('does not remove a question until the confirmation is accepted', async () => {
    vi.spyOn(api.organizer, 'getBookingPage').mockResolvedValue(
      bookingPageDetail({ formFields: [bookingFormField({ id: 'field-1', label: 'Company' })] }),
    );
    const remove = vi.spyOn(api.organizer, 'removeBookingFormField').mockResolvedValue(bookingPageDetail());

    const { user } = renderPage();
    await screen.findByText('Company');

    await user.click(screen.getByRole('button', { name: /remove question: company/i }));
    expect(remove).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'Keep it' }));

    expect(remove).not.toHaveBeenCalled();
    expect(screen.getByText('Company')).toBeInTheDocument();
  });

  it('says removing a question keeps the answers already given', async () => {
    vi.spyOn(api.organizer, 'getBookingPage').mockResolvedValue(
      bookingPageDetail({ formFields: [bookingFormField()] }),
    );

    renderPage();

    expect(await screen.findByText(/answers already given to it stay on the bookings/i)).toBeInTheDocument();
  });

  it('closes the form at the limit rather than letting the server reject the attempt', async () => {
    vi.spyOn(api.organizer, 'getBookingPage').mockResolvedValue(
      bookingPageDetail({
        formFields: Array.from({ length: MAX_FORM_FIELDS }, (_, i) =>
          bookingFormField({ id: `field-${i}`, label: `Field ${i}`, displayOrder: i }),
        ),
      }),
    );

    renderPage();

    await screen.findByText('Field 0');
    expect(screen.getByRole('button', { name: /add question/i })).toBeDisabled();
    expect(screen.getByText(new RegExp(`limit of ${MAX_FORM_FIELDS} questions`, 'i'))).toBeInTheDocument();
  });

  it("reports the server's own message when adding is rejected", async () => {
    vi.spyOn(api.organizer, 'getBookingPage').mockResolvedValue(bookingPageDetail());
    vi.spyOn(api.organizer, 'addBookingFormField').mockRejectedValue(
      new ApiError(400, { title: 'A field label is required.' }),
    );

    const { user } = renderPage();
    await screen.findByText(/no questions yet/i);

    await user.type(screen.getByLabelText(/^question$/i), 'x');
    await user.click(screen.getByRole('button', { name: /add question/i }));

    expect(await screen.findByText('A field label is required.')).toBeInTheDocument();
  });

  it('reports an unreachable server as a connection problem, not as a rejected question', async () => {
    vi.spyOn(api.organizer, 'getBookingPage').mockResolvedValue(bookingPageDetail());
    vi.spyOn(api.organizer, 'addBookingFormField').mockRejectedValue(new NetworkError());

    const { user } = renderPage();
    await screen.findByText(/no questions yet/i);

    await user.type(screen.getByLabelText(/^question$/i), 'Company');
    await user.click(screen.getByRole('button', { name: /add question/i }));

    // errorMessage stays real, so this asserts the mapping rather than a stub.
    const alert = await screen.findByRole('alert');
    expect(alert.textContent).not.toMatch(/could not add this question/i);
  });
});

/**
 * Two failure modes, and the second is the reason this page's `refresh` had to
 * catch rather than reject: it also runs after adding or removing a question,
 * where an unhandled rejection surfaced through the caller's `catch` as "Could
 * not add this question" - precisely wrong when the add succeeded.
 */
describe('BookingFormPage — the form could not be loaded', () => {
  const renderPage = () =>
    renderWithProviders(<BookingFormPage />, {
      route: `/dashboard/${PAGE_ID}/settings/form`,
      path: '/dashboard/:pageId/settings/form',
    });

  beforeEach(() => {
    resetAuthState();
  });

  it('does not claim the organizer has no questions when it could not read them', async () => {
    vi.spyOn(api.organizer, 'getBookingPage').mockRejectedValue(new NetworkError(new Error('down')));
    renderPage();

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.queryByText('No questions yet')).not.toBeInTheDocument();
    expect(screen.queryByRole('status', { name: 'Loading' })).not.toBeInTheDocument();
  });

  it('retries and renders the questions when the retry succeeds', async () => {
    const get = vi.spyOn(api.organizer, 'getBookingPage')
      .mockRejectedValueOnce(new NetworkError(new Error('down')))
      .mockResolvedValue(bookingPageDetail({ formFields: [bookingFormField({ label: 'Company' })] }));
    const { user } = renderPage();

    await screen.findByRole('alert');
    await user.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByText('Company')).toBeInTheDocument();
    expect(get).toHaveBeenCalledTimes(2);
  });

  it('reports a failed reload as a reload, not as a failure to add the question', async () => {
    vi.spyOn(api.organizer, 'getBookingPage')
      .mockResolvedValueOnce(bookingPageDetail({ formFields: [bookingFormField({ label: 'Company' })] }))
      .mockRejectedValue(new NetworkError(new Error('down')));
    vi.spyOn(api.organizer, 'addBookingFormField').mockResolvedValue(undefined as never);

    const { user } = renderPage();
    await screen.findByText('Company');

    await user.type(screen.getByLabelText('Question'), 'Job title');
    await user.click(screen.getByRole('button', { name: /add question/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not (load|reach)/i);
    expect(screen.queryByText(/could not add this question/i)).not.toBeInTheDocument();
    // The question that was already there stays on screen.
    expect(screen.getByText('Company')).toBeInTheDocument();
  });
});
