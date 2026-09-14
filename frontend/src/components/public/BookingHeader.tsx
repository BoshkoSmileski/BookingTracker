import { CalendarCheck, Clock, Globe, MapPin, Video } from 'lucide-react';
import type { ReactNode } from 'react';
import { formatCalendarDateLong, formatCalendarTimeRange } from '../../lib/calendarDates';
import { MEETING_PROVIDER_LABELS } from '../../lib/meeting';
import { formatUtcOnViewerClock, isViewerTimeZone, timeZoneLabel } from '../../lib/timezone';
import type { MeetingProvider } from '../../lib/types';
import { FOCUS_RING } from '../ui';

/**
 * A slot, on the organizer's clock - which is the clock everything in this
 * system stores, emails and writes into the `.ics`.
 *
 * `startUtc`/`endUtc` are the same slot as a real instant, supplied only where
 * the caller genuinely has one (the wizard and the reschedule flow hold an
 * `AvailableSlotDto`). They exist solely so the header can add "…your time"
 * for a visitor in another zone; nothing is scheduled from them.
 */
export interface BookingSelection {
  /** yyyy-MM-dd, organizer-local. */
  date: string;
  /** HH:mm[:ss], organizer-local. */
  startTime: string;
  endTime: string;
  startUtc?: string | null;
  endUtc?: string | null;
}

interface BookingHeaderProps {
  organizerName: string;
  title: string;
  /** Shown only while there is nothing chosen yet - see the note on `selection`. */
  description?: string | null;
  durationMinutes: number;
  meetingProvider: MeetingProvider | null;
  /** The organizer's IANA zone. Labelled, never converted from. */
  timeZoneId: string;
  /** The slot chosen so far, or null while the guest is still choosing. */
  selection?: BookingSelection | null;
  /** Renders a "Change" control beside the selection. Omit where the slot is settled. */
  onChangeSelection?: () => void;
  /** A status or reference, aligned with the organizer line. Used by the manage page. */
  aside?: ReactNode;
  /**
   * How settled the selection is.
   *
   * `active` (the default) is the accent panel: this booking is live, and the
   * highlighted line is the thing the guest came to act on. `inactive` is the
   * same line in neutral grey, for a booking that is cancelled or already past
   * - where the accent claimed a currency the booking no longer has, so a
   * cancelled appointment looked exactly like a confirmed one.
   *
   * Tone only. The layout, the content and the Change control are unchanged.
   */
  tone?: 'active' | 'inactive';
}

const META_ITEM = 'inline-flex items-center gap-1.5 whitespace-nowrap text-[13px] text-gray-700';

/**
 * What the guest is booking, above every step of the flow.
 *
 * This is the piece that replaced the wizard's first step. That step existed to
 * "select a service" on a page offering exactly one, and it made the
 * organizer's name the `<h1>` while the thing being booked was an `<h2>` inside
 * a card bordered as though it were selectable. Nothing was ever selected
 * there, so the information it carried - duration, meeting type - is simply
 * stated here instead, where it stays visible rather than scrolling out of the
 * guest's life after one click.
 *
 * The header then **absorbs the guest's choice**: once a slot is picked it
 * gains a highlighted line naming the day and the exact window, which travels
 * through the details and confirm steps and onto the confirmation. That is the
 * deliberate alternative to a static meeting sidebar - the summary is not a
 * panel repeated per step, it is the header growing as the booking takes shape,
 * which is what makes "I know exactly what I'm booking" true at every point
 * rather than only on a review screen.
 *
 * The description drops away once a slot exists, on the same principle: it is
 * what helps someone decide whether to book, and it is noise to someone who
 * already has.
 */
export function BookingHeader({
  organizerName,
  title,
  description,
  durationMinutes,
  meetingProvider,
  timeZoneId,
  selection = null,
  onChangeSelection,
  aside,
  tone = 'active',
}: BookingHeaderProps) {
  // The label map directly rather than `meetingProviderLabel`, which returns
  // null for `None` on purpose so the join panels can hide themselves. Here
  // "In person" is the organizer's actual, configured answer - it is the same
  // wording their own meeting settings screen offers - and stating it is the
  // point, because "where is this?" is a question a guest has before booking.
  //
  // `null` is NOT that answer, and must not fall back to it. A booking page
  // always carries a real value, but a *booking* carries one only once a
  // meeting has actually been created for it - so a Google Meet booking whose
  // organizer had no connected calendar, and any booking whose link the
  // backend withholds because it is cancelled or over, arrive here as null.
  // Found by running the app: those were being labelled "In person", which is
  // a claim about where to turn up that nothing in the system supports.
  const meetingLabel = meetingProvider ? (MEETING_PROVIDER_LABELS[meetingProvider] ?? null) : null;
  const MeetingIcon = meetingProvider === 'GoogleMeet' ? Video : MapPin;

  return (
    <header>
      <div className="flex items-start justify-between gap-4">
        <div className="min-w-0">
          <p className="text-[13px] font-medium text-gray-500">{organizerName}</p>
          {/* The thing being booked is the h1. The organizer is context above
              it, not the headline - a guest arrives knowing who they are
              booking with and needing to know what. */}
          <h1 className="mt-0.5 text-2xl font-semibold tracking-tight text-gray-900">{title}</h1>
        </div>
        {aside && <div className="shrink-0 pt-1">{aside}</div>}
      </div>

      {description && !selection && (
        <p className="mt-2 max-w-prose text-[15px] leading-relaxed text-gray-500">{description}</p>
      )}

      {/* Duration, where it happens, and whose clock the times are on - the
          three facts a guest needs before they can read a single slot. One row,
          stated once for the whole flow, so no later screen has to repeat them. */}
      <div className="mt-3 flex flex-wrap items-center gap-x-5 gap-y-2">
        <span className={META_ITEM}>
          <Clock className="h-4 w-4 shrink-0 text-gray-500" aria-hidden="true" />
          {durationMinutes} minutes
        </span>
        {meetingLabel && (
          <span className={META_ITEM}>
            <MeetingIcon className="h-4 w-4 shrink-0 text-gray-500" aria-hidden="true" />
            {meetingLabel}
          </span>
        )}
        <span className={META_ITEM}>
          <Globe className="h-4 w-4 shrink-0 text-gray-500" aria-hidden="true" />
          <span className="sr-only">Times shown in </span>
          {timeZoneLabel(timeZoneId)}
        </span>
      </div>

      {selection && (
        <SelectionLine
          selection={selection}
          timeZoneId={timeZoneId}
          onChangeSelection={onChangeSelection}
          tone={tone}
        />
      )}
    </header>
  );
}

const SELECTION_TONE = {
  active: { panel: 'bg-accent-50', text: 'text-accent-900', icon: 'text-accent-600', sub: 'text-accent-700' },
  inactive: { panel: 'bg-gray-100', text: 'text-gray-700', icon: 'text-gray-500', sub: 'text-gray-500' },
} as const;

function SelectionLine({
  selection,
  timeZoneId,
  onChangeSelection,
  tone,
}: {
  selection: BookingSelection;
  timeZoneId: string;
  onChangeSelection?: () => void;
  tone: 'active' | 'inactive';
}) {
  const colors = SELECTION_TONE[tone];
  const range = formatCalendarTimeRange(selection.startTime, selection.endTime);

  // Only when the two clocks genuinely differ. Telling a visitor already in the
  // organizer's zone that "10:00 is 10:00 your time" is noise that makes the
  // useful case harder to notice.
  const viewerReading =
    selection.startUtc && selection.endUtc && !isViewerTimeZone(timeZoneId)
      ? `${formatUtcOnViewerClock(selection.startUtc)} – ${formatUtcOnViewerClock(selection.endUtc)} your time`
      : null;

  return (
    <div className={`mt-4 flex flex-wrap items-start justify-between gap-x-4 gap-y-2 rounded-lg px-4 py-3 ${colors.panel}`}>
      <p className={`flex min-w-0 items-start gap-2.5 text-[15px] font-medium ${colors.text}`}>
        <CalendarCheck className={`mt-0.5 h-[18px] w-[18px] shrink-0 ${colors.icon}`} aria-hidden="true" />
        <span>
          {/* One text node with real spaces around the separator, rather than
              a styled span between two of them: a mid-dot in its own element
              contributes no spacing to the text an assistive technology reads,
              so the day and the time run together. */}
          {`${formatCalendarDateLong(selection.date)} · ${range}`}
          {viewerReading && (
            <span className={`mt-0.5 block text-[13px] font-normal ${colors.sub}`}>{viewerReading}</span>
          )}
        </span>
      </p>
      {/* `aria-label` rather than a visible word plus an `sr-only` span: the
          accessible-name algorithm trims each element's text before joining,
          so "Change" + a sibling span announces as "Changethe selected…". The
          visible word is the first word of the label, which is what WCAG's
          Label in Name requires. */}
      {onChangeSelection && (
        <button
          type="button"
          onClick={onChangeSelection}
          aria-label="Change the selected date and time"
          className={`shrink-0 rounded text-[13px] font-medium text-accent-700 underline underline-offset-2 transition-colors hover:text-accent-900 ${FOCUS_RING}`}
        >
          Change
        </button>
      )}
    </div>
  );
}
