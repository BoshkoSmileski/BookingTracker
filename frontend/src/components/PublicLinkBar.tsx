import { ExternalLink, Link2 } from 'lucide-react';
import { COPIED_LABEL, useCopyToClipboard } from '../hooks/useCopyToClipboard';
import { BUTTON_GHOST, CARD } from './ui';

/**
 * A booking page's public link, on the booking page's own screen.
 *
 * "Share my booking page" is the product's core job, and the URL was reachable
 * from exactly two places: the workspace card grid at `/dashboard`, and
 * `NewBookingPagePanel` - which appears **once**, immediately after creation,
 * and never again. So an organizer working on a page they made last week had to
 * navigate away from it to find its address, or reconstruct the URL from the
 * slug on the Details screen.
 *
 * Deliberately a quiet strip rather than a panel: it sits above a dashboard
 * that already has a headline band, and the link is something you come back for
 * occasionally, not the reason you opened the screen. It is suppressed entirely
 * while `NewBookingPagePanel` is showing, which states the same URL louder and
 * with more context.
 */
export function PublicLinkBar({ slug }: { slug: string }) {
  const { copied, copy } = useCopyToClipboard();
  const publicUrl = `${window.location.origin}/book/${slug}`;

  return (
    <div className={`${CARD} mb-6 flex flex-wrap items-center gap-x-3 gap-y-2 px-4 py-3`}>
      <span className="text-[13px] font-medium text-gray-500">Public link</span>
      {/* `min-w-0` + `truncate` so a long slug shortens instead of pushing the
          two actions off the row at phone widths. */}
      <code className="min-w-0 flex-1 truncate text-[13px] text-gray-700">{publicUrl}</code>
      <div className="flex shrink-0 items-center gap-0.5">
        <button type="button" onClick={() => void copy(publicUrl)} className={BUTTON_GHOST}>
          <Link2 className="h-4 w-4" aria-hidden="true" />
          {copied === publicUrl ? COPIED_LABEL : 'Copy link'}
        </button>
        <a href={`/book/${slug}`} target="_blank" rel="noreferrer" className={BUTTON_GHOST}>
          <ExternalLink className="h-4 w-4" aria-hidden="true" />
          {/* The visible word is the first word of the accessible name, per
              WCAG's Label in Name - and the label says where it goes, because
              "Preview" alone does not say it opens the public page. */}
          <span aria-hidden="true">Preview</span>
          <span className="sr-only">Preview the public booking page in a new tab</span>
        </a>
      </div>
    </div>
  );
}
