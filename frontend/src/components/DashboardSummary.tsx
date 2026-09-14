import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { AlertTriangle, CalendarClock, CalendarOff, Sun } from 'lucide-react';
import { useAuth } from '../contexts/AuthContext';
import { api, errorMessage } from '../lib/api';
import { hasBookableAvailability } from '../lib/availability';
import {
  formatCalendarDate,
  formatCalendarTime,
  organizerDayOfWeek,
  organizerToday,
} from '../lib/calendarDates';
import { DAY_DISPLAY_ORDER, DAY_NAMES } from '../lib/dayOfWeek';
import { CARD, FOCUS_RING, LoadError, META, SECTION_HEADING } from './ui';
import type { ComponentType, ReactNode } from 'react';
import type { AvailabilityExceptionDto, BookingSessionDto, WorkingScheduleDto } from '../lib/types';

interface DashboardSummaryProps {
  pageId: string;
}

/**
 * The lower bound for the blocked-dates query, one day earlier than the
 * browser's date.
 *
 * The organizer's zone is not known until the schedule in the same batch of
 * requests comes back, and no two zones are ever more than about 26 hours
 * apart - so widening by a day guarantees the organizer's own today is inside
 * the window whichever side of it the browser sits. The list is then filtered
 * precisely, against the organizer's date, once the schedule has arrived.
 */
function exceptionQueryFrom(): string {
  const d = new Date();
  d.setDate(d.getDate() - 1);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

function formatIntervals(day: WorkingScheduleDto['days'][number]): string {
  if (!day.isEnabled || day.intervals.length === 0) return 'Closed';
  return day.intervals.map((i) => `${formatCalendarTime(i.start)}-${formatCalendarTime(i.end)}`).join(', ');
}

/**
 * Dashboard-at-a-glance for one booking page.
 *
 * Deliberately unequal. An earlier version gave four figures and four lists the
 * same size, the same border and the same weight, which meant the screen
 * answered no question faster than any other — everything competed. The order
 * here is what an organizer opening the app actually wants to know:
 *
 *   1. What is happening today          — the largest thing on the screen
 *   2. What is coming up next           — beside it, same panel weight
 *   3. Totals for this page             — a headline band, read once
 *   4. Schedule shape and blocked days  — reference, quiet, at the bottom
 */
export function DashboardSummary({ pageId }: DashboardSummaryProps) {
  const { callProtected } = useAuth();
  const [active, setActive] = useState<BookingSessionDto[]>([]);
  const [submitted, setSubmitted] = useState<BookingSessionDto[]>([]);
  const [abandoned, setAbandoned] = useState<BookingSessionDto[]>([]);
  const [exceptions, setExceptions] = useState<AvailabilityExceptionDto[]>([]);
  const [schedule, setSchedule] = useState<WorkingScheduleDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  /**
   * All five together, and caught together.
   *
   * Every figure below is derived by counting the arrays these fill, so a
   * failure that resolved instead of rejecting would render a complete,
   * confident band of zeroes - "0 confirmed bookings", "no bookable times yet",
   * with an amber banner telling the organizer to go and set working hours they
   * have already set. Without a `catch` at all it simply skeletoned for ever.
   */
  const load = useCallback(() => {
    setLoading(true);
    setLoadError(null);
    Promise.all([
      callProtected((token) => api.organizer.getSessions(token, pageId, 'Active')),
      callProtected((token) => api.organizer.getSessions(token, pageId, 'Submitted')),
      callProtected((token) => api.organizer.getSessions(token, pageId, 'Abandoned')),
      callProtected((token) => api.availability.getExceptions(token, exceptionQueryFrom())),
      callProtected((token) => api.availability.getSchedule(token)),
    ])
      .then(([activeSessions, submittedSessions, abandonedSessions, exc, sched]) => {
        setActive(activeSessions);
        setSubmitted(submittedSessions);
        setAbandoned(abandonedSessions);
        setExceptions(exc);
        setSchedule(sched);
      })
      .catch((e) => setLoadError(errorMessage(e, 'Could not load this summary.')))
      .finally(() => setLoading(false));
  }, [callProtected, pageId]);

  useEffect(() => {
    load();
  }, [load]);

  if (loading) {
    return (
      <div className="mb-10 space-y-4" role="status" aria-label="Loading">
        <div className={`${CARD} h-24 animate-pulse`} />
        <div className={`${CARD} h-56 animate-pulse`} />
      </div>
    );
  }

  // The session list underneath is a separate request and may well have
  // succeeded, so this reports only itself rather than taking the screen.
  if (loadError) {
    return <LoadError message={loadError} onRetry={load} className="mb-10" />;
  }

  // Today on the ORGANIZER's clock, not the browser's. Every comparison below
  // is against `selectedDate`/`selectedTime`, which are organizer wall-clock
  // columns - so a browser in another zone used to shift this whole panel by a
  // day, and near midnight it did so for everyone.
  const today = organizerToday(schedule?.timeZoneId);
  const todaysAppointments = submitted
    .filter((s) => s.selectedDate === today)
    .sort((a, b) => (a.selectedTime ?? '').localeCompare(b.selectedTime ?? ''));
  const upcoming = submitted
    .filter((s) => (s.selectedDate ?? '') > today)
    .sort((a, b) => `${a.selectedDate}${a.selectedTime}`.localeCompare(`${b.selectedDate}${b.selectedTime}`))
    .slice(0, 6);
  // Filtered here rather than by the query, whose bound is deliberately a day
  // wide - see exceptionQueryFrom.
  const upcomingBlocked = exceptions.filter((e) => (e.endDate ?? e.date) >= today).slice(0, 4);

  const totalSessions = active.length + submitted.length + abandoned.length;
  const completionRate = totalSessions === 0 ? null : Math.round((submitted.length / totalSessions) * 100);
  const todaysSchedule = schedule?.days.find((d) => d.dayOfWeek === organizerDayOfWeek(schedule.timeZoneId));

  return (
    <div className="mb-10 space-y-6">
      {!hasBookableAvailability(schedule) && (
        <div className="flex flex-wrap items-center justify-between gap-4 rounded-lg border border-amber-200 bg-amber-50 px-5 py-4">
          <div className="flex items-start gap-3">
            <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-amber-600" aria-hidden="true" />
            <p className="text-[15px] text-amber-900">
              This page has no bookable times yet &mdash; guests can&rsquo;t book until you set your working hours.
            </p>
          </div>
          <Link
            to={`/dashboard/${pageId}/settings/hours`}
            className={`shrink-0 rounded-lg bg-amber-600 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-amber-700 ${FOCUS_RING}`}
          >
            Set working hours
          </Link>
        </div>
      )}

      {/* The headline band. One object with dividing rules, not four tiles with
          gaps: these four figures describe the same thing and are read together,
          and four separate borders said they were four unrelated cards. */}
      {/* `divide-x` is applied only at the width where this is genuinely one
          row. In the 2x2 state it drew a vertical rule down the left edge of
          the third cell - the first of the second row - which read as a stray
          line rather than as a divider, with no horizontal rule to pair it. */}
      <dl className={`grid grid-cols-2 ${CARD} divide-gray-200 lg:grid-cols-4 lg:divide-x`}>
        <Figure label="Confirmed bookings" value={submitted.length} emphasis />
        <Figure label="Filling in now" value={active.length} emphasis />
        <Figure label="Abandoned" value={abandoned.length} />
        <Figure
          label="Completion rate"
          value={completionRate === null ? '—' : `${completionRate}%`}
          hint={`of ${totalSessions} session${totalSessions === 1 ? '' : 's'}`}
        />
      </dl>

      {/* Today and Upcoming dominate: they are the two questions this screen
          exists to answer, so they get the width, the padding and the type size.
          `items-start` so an empty Today sizes to its one line instead of being
          stretched to match a full Upcoming — that stretch was ~200px of blank
          card, and it pushed the session list this page is named for off the
          bottom of the screen. */}
      <div className="grid items-start gap-6 lg:grid-cols-2">
        <Panel icon={Sun} title="Today" caption={todaysSchedule ? formatIntervals(todaysSchedule) : 'Not configured'}>
          {todaysAppointments.length === 0 ? (
            // An empty day is the common case, and "Nothing booked today." on
            // its own left a tall blank card beside a full Upcoming. Naming the
            // next appointment answers the question the organizer asks straight
            // afterwards, from data this component already holds.
            <div className="py-2">
              <p className="text-sm text-gray-500">Nothing booked today.</p>
              {upcoming[0] && (
                <p className="mt-1 text-sm text-gray-900">
                  Next:{' '}
                  <span className="font-medium">
                    {upcoming[0].selectedDate && formatCalendarDate(upcoming[0].selectedDate)}
                    {upcoming[0].selectedTime ? `, ${formatCalendarTime(upcoming[0].selectedTime)}` : ''}
                  </span>
                  {upcoming[0].name ? ` with ${upcoming[0].name}` : ''}
                </p>
              )}
            </div>
          ) : (
            <ul className="divide-y divide-gray-100">
              {todaysAppointments.map((s) => (
                <AppointmentRow
                  key={s.id}
                  when={s.selectedTime ? formatCalendarTime(s.selectedTime) : ''}
                  who={s.name}
                  detail={s.email}
                />
              ))}
            </ul>
          )}
        </Panel>

        <Panel icon={CalendarClock} title="Upcoming" caption={`${upcoming.length} of ${submitted.length} confirmed`}>
          {upcoming.length === 0 ? (
            <Nothing>Nothing on the books yet.</Nothing>
          ) : (
            <ul className="divide-y divide-gray-100">
              {upcoming.map((s) => (
                <AppointmentRow
                  key={s.id}
                  when={s.selectedDate ? formatCalendarDate(s.selectedDate) : ''}
                  who={s.name}
                  detail={s.selectedTime ? formatCalendarTime(s.selectedTime) : null}
                />
              ))}
            </ul>
          )}
        </Panel>
      </div>

      {/*
        Reference material: true, occasionally useful, never the reason you
        opened the screen — so it is a single quiet strip rather than two tall
        columns.

        As two stacked lists it ran to ~250px and pushed the session list below
        the fold, which inverted the page: a screen called "Sessions" showed
        everything except them. Laid out horizontally the same facts take a
        third of the height, and the day/hours pairs sit next to each other
        instead of being flung apart by `justify-between` across a 1400px row.
      */}
      <div className="flex flex-wrap gap-x-12 gap-y-5 border-t border-gray-200 pt-5">
        <div className="min-w-0">
          {/* "Blocked dates" was the name of a screen that no longer exists -
              it and "Date overrides" were consolidated into Date exceptions.
              Naming this strip after the merged screen would be worse than
              stale, though: it lists only the subtractive half, so it would
              claim to be a list it is not. "Unavailable dates" is the word the
              consolidated screen itself puts on exactly these rows. */}
          <h3 className={`mb-2 flex items-center gap-2 ${META}`}>
            <CalendarOff className="h-4 w-4 text-gray-400" aria-hidden="true" />
            Unavailable dates
          </h3>
          {upcomingBlocked.length === 0 ? (
            <p className="text-sm text-gray-500">None coming up.</p>
          ) : (
            <ul className="flex flex-wrap gap-x-5 gap-y-1.5">
              {upcomingBlocked.map((e) => (
                <li key={e.id} className="text-sm">
                  <span className="text-gray-900">{formatCalendarDate(e.date)}</span>
                  <span className="ml-1.5 text-gray-500">{e.type}</span>
                </li>
              ))}
            </ul>
          )}
        </div>

        <div className="min-w-0">
          <h3 className={`mb-2 ${META}`}>Weekly availability</h3>
          {!schedule ? (
            <p className="text-sm text-gray-500">Not configured yet.</p>
          ) : (
            <ul className="flex flex-wrap gap-x-5 gap-y-1.5">
              {DAY_DISPLAY_ORDER.map((day) => {
                const config = schedule.days.find((d) => d.dayOfWeek === day);
                const open = config?.isEnabled ?? false;
                return (
                  <li key={day} className="text-sm">
                    <span className={open ? 'font-medium text-gray-900' : 'text-gray-500'}>
                      {DAY_NAMES[day].slice(0, 3)}
                    </span>
                    <span className={`ml-1.5 tabular-nums text-gray-500`}>
                      {config ? formatIntervals(config) : 'Closed'}
                    </span>
                  </li>
                );
              })}
            </ul>
          )}
        </div>
      </div>
    </div>
  );
}

/**
 * One headline figure. `emphasis` is what makes the band a hierarchy rather
 * than a row of equals — the two numbers an organizer checks first are set
 * larger and in the accent; the other two are context.
 */
function Figure({
  label,
  value,
  hint,
  emphasis,
}: {
  label: string;
  value: string | number;
  hint?: string;
  emphasis?: boolean;
}) {
  // Accent only when there is something to report: a green 0 claims a good
  // result where there is simply no result, and it spends the one colour on the
  // screen on the least informative cell in the band.
  const isZero = value === 0 || value === '0' || value === '—';
  return (
    <div className="px-5 py-4">
      <dt className="text-[13px] font-medium text-gray-500">{label}</dt>
      <dd
        className={`mt-1 font-semibold tabular-nums tracking-tight ${
          emphasis ? 'text-4xl' : 'text-3xl'
        } ${emphasis && !isZero ? 'text-accent-700' : isZero ? 'text-gray-500' : 'text-gray-900'}`}
      >
        {value}
      </dd>
      {hint && <p className="mt-0.5 text-[13px] text-gray-500">{hint}</p>}
    </div>
  );
}

function Panel({
  icon: IconComponent,
  title,
  caption,
  children,
}: {
  icon: ComponentType<{ className?: string }>;
  title: string;
  caption?: string;
  children: ReactNode;
}) {
  return (
    <section className={`${CARD} p-5`}>
      <div className="mb-3 flex items-baseline justify-between gap-3">
        <h3 className={`flex items-center gap-2 ${SECTION_HEADING}`}>
          <IconComponent className="h-[18px] w-[18px] text-accent-600" aria-hidden="true" />
          {title}
        </h3>
        {caption && <span className={`shrink-0 truncate tabular-nums ${META}`}>{caption}</span>}
      </div>
      {children}
    </section>
  );
}

/** A booked slot: the time leads, because that is what you scan a day by. */
function AppointmentRow({ when, who, detail }: { when: string; who: string | null; detail: string | null }) {
  return (
    <li className="flex items-baseline gap-4 py-2.5 first:pt-0 last:pb-0">
      <span className="w-24 shrink-0 text-sm font-semibold tabular-nums text-gray-900">{when}</span>
      <span className="min-w-0 flex-1 truncate text-sm text-gray-900">{who || 'Anonymous visitor'}</span>
      {detail && <span className={`shrink-0 truncate tabular-nums ${META}`}>{detail}</span>}
    </li>
  );
}

function Nothing({ children }: { children: ReactNode }) {
  return <p className="py-2 text-sm text-gray-500">{children}</p>;
}
