import { screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { PublicLinkBar } from '../PublicLinkBar';
import { renderWithProviders } from '../../test/render';

/**
 * "Share my booking page" is the product's core job, and the URL was reachable
 * from exactly two places: the workspace card grid, and a panel that appears
 * once immediately after a page is created and never again. An organizer
 * working on a page they made last week had to navigate away from it to find
 * its address.
 */
describe('PublicLinkBar', () => {
  it('shows the page’s public URL', () => {
    renderWithProviders(<PublicLinkBar slug="demo-30-min-meeting" />);

    expect(screen.getByText(`${window.location.origin}/book/demo-30-min-meeting`)).toBeInTheDocument();
  });

  it('opens the public page in a new tab, and says so in the accessible name', () => {
    renderWithProviders(<PublicLinkBar slug="demo-30-min-meeting" />);

    const preview = screen.getByRole('link', { name: /preview the public booking page/i });
    expect(preview).toHaveAttribute('href', '/book/demo-30-min-meeting');
    expect(preview).toHaveAttribute('target', '_blank');
    // The visible word is the first word of the accessible name (Label in Name).
    expect(preview).toHaveTextContent('Preview');
  });

  it('copies the full URL and echoes it briefly', async () => {
    // Spy on the stub user-event's setup() already installed, rather than
    // replacing `navigator.clipboard` - it is a getter-only property, and
    // reassigning it breaks the render rather than the clipboard. Same approach
    // as MeetingPanel's own copy test.
    const { user } = renderWithProviders(<PublicLinkBar slug="coffee-chat" />);
    const writeText = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue(undefined);

    await user.click(screen.getByRole('button', { name: /copy link/i }));

    expect(writeText).toHaveBeenCalledWith(`${window.location.origin}/book/coffee-chat`);
    expect(await screen.findByRole('button', { name: /copied/i })).toBeInTheDocument();
  });
});
