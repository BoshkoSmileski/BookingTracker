import { screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MeetingPanel } from '../MeetingPanel';
import { renderWithProviders } from '../../test/render';

const MEET_URL = 'https://meet.google.com/abc-defg-hij';

describe('MeetingPanel', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('shows the provider, the URL and a join link that opens the meeting', () => {
    renderWithProviders(<MeetingPanel meetingProvider="GoogleMeet" meetingUrl={MEET_URL} />);

    expect(screen.getByText('Google Meet')).toBeInTheDocument();
    expect(screen.getByText(MEET_URL)).toBeInTheDocument();

    const join = screen.getByRole('link', { name: 'Join Google Meet' });
    expect(join).toHaveAttribute('href', MEET_URL);
    // A meeting opens beside the app rather than replacing it - a guest who
    // navigates away from the manage page loses their way back to it.
    expect(join).toHaveAttribute('target', '_blank');
    expect(join).toHaveAttribute('rel', expect.stringContaining('noopener'));
  });

  it('renders nothing for an in-person booking, so a screen can drop it in unconditionally', () => {
    const { container } = renderWithProviders(<MeetingPanel meetingProvider={null} meetingUrl={null} />);

    expect(container).toBeEmptyDOMElement();
  });

  it('renders nothing when a meeting was wanted but no link was ever created', () => {
    // Google unavailable, or no connected calendar. A button here would lead
    // nowhere, which is worse than showing none.
    const { container } = renderWithProviders(<MeetingPanel meetingProvider="GoogleMeet" meetingUrl={null} />);

    expect(container).toBeEmptyDOMElement();
  });

  // Spied on AFTER rendering, never stubbed as a whole navigator: user-event's
  // setup() installs its own clipboard stub during render, and replacing the
  // navigator object wholesale drops the prototype getters it needs, which
  // breaks the render rather than the clipboard.
  it('copies the link to the clipboard and confirms it', async () => {
    const { user } = renderWithProviders(<MeetingPanel meetingProvider="GoogleMeet" meetingUrl={MEET_URL} />);
    const writeText = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue(undefined);

    await user.click(screen.getByRole('button', { name: 'Copy link' }));

    expect(writeText).toHaveBeenCalledWith(MEET_URL);
    expect(await screen.findByRole('button', { name: 'Copied!' })).toBeInTheDocument();
  });

  it('survives a refused clipboard without breaking the panel', async () => {
    // Clipboard access is refused outside a secure context. The URL is on
    // screen either way, so the fallback is selecting it by hand - never an
    // error state.
    const { user } = renderWithProviders(<MeetingPanel meetingProvider="GoogleMeet" meetingUrl={MEET_URL} />);
    vi.spyOn(navigator.clipboard, 'writeText').mockRejectedValue(new Error('denied'));

    await user.click(screen.getByRole('button', { name: 'Copy link' }));

    expect(screen.getByRole('button', { name: 'Copy link' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Join Google Meet' })).toHaveAttribute('href', MEET_URL);
  });
});
