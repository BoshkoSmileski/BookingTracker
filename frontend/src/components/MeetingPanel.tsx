import { Check, Copy, Video } from 'lucide-react';
import { COPIED_LABEL, useCopyToClipboard } from '../hooks/useCopyToClipboard';
import { BUTTON_PRIMARY, BUTTON_SECONDARY, CARD, META } from './ui';
import { hasMeeting, meetingProviderLabel } from '../lib/meeting';
import type { MeetingProvider } from '../lib/types';

interface MeetingPanelProps {
  meetingProvider: MeetingProvider | null;
  meetingUrl: string | null;
  className?: string;
}

/**
 * A booking's online meeting: what kind it is, a Join button, and Copy link.
 *
 * Renders nothing when the booking has no meeting, so a screen can drop it in
 * unconditionally rather than each caller repeating the same two-part check -
 * the same shape `BookingInstructions` uses for a page with no instructions.
 *
 * Copy exists because the link is routinely needed somewhere that is not this
 * browser: pasted into a chat, forwarded to a colleague, or read out. Joining
 * and copying are genuinely different actions, so the copy control is a button
 * rather than "select the text yourself".
 */
export function MeetingPanel({ meetingProvider, meetingUrl, className = '' }: MeetingPanelProps) {
  // The hook is called before the early return below, as the rules of hooks
  // require - it holds no per-booking state, so there is nothing to reset when
  // the panel renders for a different meeting.
  const { copied, copy } = useCopyToClipboard();

  if (!hasMeeting({ meetingProvider, meetingUrl })) return null;

  const label = meetingProviderLabel(meetingProvider) ?? 'meeting';
  const url = meetingUrl!;
  const isCopied = copied === url;

  return (
    // A named group: the URL, Join and Copy are one set of related controls,
    // and naming it lets a screen-reader user jump to "Online meeting" rather
    // than discovering a bare link between the booking details and the manage
    // actions. It is also what lets a test scope to this panel on a screen
    // where the provider name legitimately appears twice.
    <div role="group" aria-label="Online meeting" className={`${CARD} p-5 ${className}`}>
      <div className="flex items-center gap-2">
        <Video className="h-4 w-4 shrink-0 text-gray-500" aria-hidden="true" />
        <span className="text-[15px] font-medium text-gray-900">{label}</span>
      </div>

      <p className={`mt-1 break-all ${META}`}>{url}</p>

      <div className="mt-3 flex flex-wrap gap-2">
        {/* BUTTON_PRIMARY rather than a secondary button with the accent
            appended: joining is the action this panel exists for, and
            overriding a base class's own colours by appending is the Tailwind
            precedence trap this codebase has already been caught by once -
            stylesheet order decides, not attribute order. */}
        {/* Named after the thing it opens - "Join Google Meet" - rather than
            the generic "Join meeting" this used to say. The provider is
            already printed above, but a button's own label is what a screen
            reader announces out of context, and it is the label a guest scans
            an email or a page for. */}
        <a href={url} target="_blank" rel="noreferrer noopener" className={BUTTON_PRIMARY}>
          Join {label}
        </a>
        <button type="button" onClick={() => void copy(url)} className={BUTTON_SECONDARY}>
          {isCopied ? (
            <>
              <Check className="h-4 w-4" aria-hidden="true" />
              {COPIED_LABEL}
            </>
          ) : (
            <>
              <Copy className="h-4 w-4" aria-hidden="true" />
              Copy link
            </>
          )}
        </button>
      </div>

      {/* Announced without stealing focus, so a screen-reader user gets the
          same confirmation the sighted "Copied" state gives. */}
      <span role="status" className="sr-only">
        {isCopied ? 'Meeting link copied to clipboard' : ''}
      </span>
    </div>
  );
}
