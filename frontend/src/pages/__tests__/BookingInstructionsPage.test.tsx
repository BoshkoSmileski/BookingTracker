import { screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { BookingInstructionsPage } from '../BookingInstructionsPage';
import { api } from '../../lib/api';
import { bookingInstruction, bookingPageDetail } from '../../test/factories';
import { renderWithProviders } from '../../test/render';
import { resetAuthState } from '../../test/authContextMock';

vi.mock('../../contexts/AuthContext', () => import('../../test/authContextMock'));

const PAGE_ID = '11111111-1111-1111-1111-111111111111';

/**
 * This screen was "Booking questions" and had to explain in its own subtitle
 * that the questions were not answered. These tests pin the wording as much as
 * the behaviour: the whole point of the rename is that nothing on the page
 * suggests a visitor types anything back.
 */
describe('BookingInstructionsPage', () => {
  const renderPage = () =>
    renderWithProviders(<BookingInstructionsPage />, {
      route: `/dashboard/${PAGE_ID}/settings/instructions`,
      path: '/dashboard/:pageId/settings/instructions',
    });

  beforeEach(() => {
    resetAuthState();
  });

  it('describes what the feature is for, in instruction terms', async () => {
    vi.spyOn(api.organizer, 'getBookingPage').mockResolvedValue(bookingPageDetail());
    renderPage();

    expect(await screen.findByRole('heading', { name: /booking instructions/i })).toBeInTheDocument();
    expect(screen.getByText(/display important information for visitors before they complete a booking/i))
      .toBeInTheDocument();
  });

  it('never calls them questions', async () => {
    vi.spyOn(api.organizer, 'getBookingPage').mockResolvedValue(
      bookingPageDetail({ instructions: [bookingInstruction()] }),
    );
    const { container } = renderPage();

    await screen.findByText('Please have your account number ready');
    // The old wording is what made the screen confusing in the first place.
    expect(container.textContent).not.toMatch(/question/i);
    expect(container.textContent).not.toMatch(/prompt/i);
  });

  it('invites the organizer to add one when the list is empty', async () => {
    vi.spyOn(api.organizer, 'getBookingPage').mockResolvedValue(bookingPageDetail());
    renderPage();

    expect(await screen.findByText(/no instructions yet/i)).toBeInTheDocument();
    expect(screen.getByText(/appears on your booking page before a visitor confirms/i)).toBeInTheDocument();
  });

  it('lists existing instructions in the order the backend sent them', async () => {
    vi.spyOn(api.organizer, 'getBookingPage').mockResolvedValue(
      bookingPageDetail({
        instructions: [
          bookingInstruction({ id: 'a', text: 'Bring photo ID', displayOrder: 0 }),
          bookingInstruction({ id: 'b', text: 'Arrive five minutes early', displayOrder: 1 }),
        ],
      }),
    );
    renderPage();

    const items = await screen.findAllByRole('listitem');
    expect(items.map((li) => li.textContent)).toEqual([
      expect.stringContaining('Bring photo ID'),
      expect.stringContaining('Arrive five minutes early'),
    ]);
  });

  it('adds an instruction and refreshes the list', async () => {
    const get = vi.spyOn(api.organizer, 'getBookingPage')
      .mockResolvedValueOnce(bookingPageDetail())
      .mockResolvedValue(bookingPageDetail({ instructions: [bookingInstruction({ text: 'Bring photo ID' })] }));
    const add = vi.spyOn(api.organizer, 'addBookingInstruction').mockResolvedValue(bookingPageDetail());
    const { user } = renderPage();

    await screen.findByText(/no instructions yet/i);
    await user.type(screen.getByLabelText(/^instruction$/i), 'Bring photo ID');
    await user.click(screen.getByRole('button', { name: /add instruction/i }));

    await waitFor(() => expect(add).toHaveBeenCalledWith(expect.any(String), PAGE_ID, 'Bring photo ID'));
    expect(await screen.findByText('Bring photo ID')).toBeInTheDocument();
    expect(get).toHaveBeenCalledTimes(2);
  });

  it('removes an instruction', async () => {
    vi.spyOn(api.organizer, 'getBookingPage')
      .mockResolvedValueOnce(bookingPageDetail({ instructions: [bookingInstruction({ id: 'a', text: 'Bring photo ID' })] }))
      .mockResolvedValue(bookingPageDetail());
    const remove = vi.spyOn(api.organizer, 'removeBookingInstruction').mockResolvedValue(bookingPageDetail());
    const { user } = renderPage();

    await user.click(await screen.findByRole('button', { name: /remove instruction: bring photo id/i }));
    // Removing is destructive, so it asks first.
    await user.click(screen.getByRole('button', { name: 'Remove instruction' }));

    await waitFor(() => expect(remove).toHaveBeenCalledWith(expect.any(String), PAGE_ID, 'a'));
    expect(await screen.findByText(/no instructions yet/i)).toBeInTheDocument();
  });

  it('does not remove an instruction until the confirmation is accepted', async () => {
    vi.spyOn(api.organizer, 'getBookingPage')
      .mockResolvedValue(bookingPageDetail({ instructions: [bookingInstruction({ id: 'a', text: 'Bring photo ID' })] }));
    const remove = vi.spyOn(api.organizer, 'removeBookingInstruction').mockResolvedValue(bookingPageDetail());
    const { user } = renderPage();

    await user.click(await screen.findByRole('button', { name: /remove instruction: bring photo id/i }));
    expect(remove).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'Keep it' }));

    expect(remove).not.toHaveBeenCalled();
    expect(screen.getByText('Bring photo ID')).toBeInTheDocument();
  });

  it('surfaces the server’s message when adding fails', async () => {
    const { ApiError } = await import('../../lib/api');
    vi.spyOn(api.organizer, 'getBookingPage').mockResolvedValue(bookingPageDetail());
    vi.spyOn(api.organizer, 'addBookingInstruction')
      .mockRejectedValue(new ApiError(400, { title: 'An instruction cannot be longer than 300 characters.' }));
    const { user } = renderPage();

    await screen.findByText(/no instructions yet/i);
    await user.type(screen.getByLabelText(/^instruction$/i), 'x');
    await user.click(screen.getByRole('button', { name: /add instruction/i }));

    expect(await screen.findByRole('alert'))
      .toHaveTextContent('An instruction cannot be longer than 300 characters.');
  });

  it('reports an unreachable server as connectivity, not as a rejected instruction', async () => {
    const { NetworkError } = await import('../../lib/api');
    vi.spyOn(api.organizer, 'getBookingPage').mockResolvedValue(bookingPageDetail());
    vi.spyOn(api.organizer, 'addBookingInstruction').mockRejectedValue(new NetworkError());
    const { user } = renderPage();

    await screen.findByText(/no instructions yet/i);
    await user.type(screen.getByLabelText(/^instruction$/i), 'x');
    await user.click(screen.getByRole('button', { name: /add instruction/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not reach the server/i);
  });
});

/** Same two failure modes as BookingFormPage - see the note there. */
describe('BookingInstructionsPage — the instructions could not be loaded', () => {
  const renderPage = () =>
    renderWithProviders(<BookingInstructionsPage />, {
      route: `/dashboard/${PAGE_ID}/settings/instructions`,
      path: '/dashboard/:pageId/settings/instructions',
    });

  beforeEach(() => {
    resetAuthState();
  });

  it('does not claim there are no instructions when it could not read them', async () => {
    vi.spyOn(api.organizer, 'getBookingPage').mockRejectedValue(new Error('boom'));
    renderPage();

    expect(await screen.findByRole('alert')).toBeInTheDocument();
    expect(screen.queryByText('No instructions yet')).not.toBeInTheDocument();
    expect(screen.queryByRole('status', { name: 'Loading' })).not.toBeInTheDocument();
  });

  it('retries and renders the instructions when the retry succeeds', async () => {
    const get = vi.spyOn(api.organizer, 'getBookingPage')
      .mockRejectedValueOnce(new Error('boom'))
      .mockResolvedValue(bookingPageDetail({
        instructions: [bookingInstruction({ text: 'Bring your account number' })],
      }));
    const { user } = renderPage();

    await screen.findByRole('alert');
    await user.click(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByText('Bring your account number')).toBeInTheDocument();
    expect(get).toHaveBeenCalledTimes(2);
  });
});
