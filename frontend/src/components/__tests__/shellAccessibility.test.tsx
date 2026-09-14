import { screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { AuthShell } from '../AuthShell';
import { PublicMessage, PublicShell } from '../public/PublicShell';
import { MAIN_CONTENT_ID, SkipLink } from '../ui';
import { renderWithProviders } from '../../test/render';

/**
 * The two structural accessibility guarantees every screen in this app now
 * makes: exactly one `main` landmark, and a keyboard route straight to it.
 *
 * The guest flow - the one screen an organizer's own customers use - had
 * neither. A screen-reader user jumping by landmark found nothing to jump to,
 * and a keyboard user had no way past the navigation on any screen.
 */
describe('shell landmarks and the skip link', () => {
  it('gives the public booking sheet a single main landmark', () => {
    renderWithProviders(
      <PublicShell>
        <p>Booking content</p>
      </PublicShell>,
    );

    const main = screen.getByRole('main');
    expect(main).toHaveAttribute('id', MAIN_CONTENT_ID);
    expect(main).toHaveTextContent('Booking content');
    // Exactly one - a nested second landmark makes "jump to main" ambiguous.
    expect(screen.getAllByRole('main')).toHaveLength(1);
  });

  it('offers a skip link that targets that main, first in the tab order', () => {
    renderWithProviders(
      <PublicShell>
        <button type="button">Something focusable inside the page</button>
      </PublicShell>,
    );

    const skip = screen.getByRole('link', { name: /skip to main content/i });
    expect(skip).toHaveAttribute('href', `#${MAIN_CONTENT_ID}`);
    // The destination must be focusable, or the link scrolls without moving
    // focus and the next Tab continues from wherever it already was.
    expect(screen.getByRole('main')).toHaveAttribute('tabindex', '-1');
    // Before the page's own controls in document order.
    expect(skip.compareDocumentPosition(screen.getByRole('button'))).toBe(
      Node.DOCUMENT_POSITION_FOLLOWING,
    );
  });

  it('is not rendered visibly until it is focused', async () => {
    // `sr-only` alone would leave it invisible to the sighted keyboard user it
    // exists for; the class list has to promote it on focus.
    const { user } = renderWithProviders(<SkipLink />);
    const skip = screen.getByRole('link', { name: /skip to main content/i });

    expect(skip.className).toContain('sr-only');
    expect(skip.className).toContain('focus:not-sr-only');

    await user.tab();
    expect(skip).toHaveFocus();
  });

  it('gives the whole-screen public message a main landmark too', () => {
    renderWithProviders(<PublicMessage title="Page not found">Nothing here.</PublicMessage>);

    expect(screen.getByRole('main')).toHaveAttribute('id', MAIN_CONTENT_ID);
    expect(screen.getByRole('heading', { name: 'Page not found' })).toBeInTheDocument();
  });

  it('gives the sign-in surface a main landmark', () => {
    renderWithProviders(
      <AuthShell title="Sign in">
        <button type="button">Continue</button>
      </AuthShell>,
    );

    const main = screen.getByRole('main');
    expect(main).toHaveAttribute('id', MAIN_CONTENT_ID);
    expect(main).toHaveTextContent('Sign in');
  });
});
