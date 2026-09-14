import { useCallback, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import {
  Activity, BarChart3, CalendarCheck2, Clock, Download, FileSpreadsheet, FileText,
  Mail, TrendingUp, TriangleAlert,
} from 'lucide-react';
import { FunnelChart, HourChart, StatusDoughnut, TrendChart, WeekdayChart } from '../components/analytics/Charts';
import { EmptyState, MeterRow, Panel, SkeletonCards, SkeletonPanel, StatCard } from '../components/analytics/Panels';
import { BUTTON_SECONDARY, FOCUS_RING, INPUT, InlineNotice, LoadError, PageHeader } from '../components/ui';
import { formatDuration, formatPercent } from '../components/analytics/chartTheme';
import { useAuth } from '../contexts/AuthContext';
import { api, errorMessage } from '../lib/api';
import { formatDateTime, formatRelative } from '../lib/dates';
import { saveFile } from '../lib/download';
import { useDocumentTitle } from '../lib/pageTitle';
import type {
  ActivityEntryDto, AnalyticsExportFormat, AnalyticsFilter, BookingAnalyticsDto,
  BookingPageSummaryDto, BookingSessionStatus, ConversionFunnelDto, OperationalAnalyticsDto,
} from '../lib/types';

type RangeKey = '7d' | '30d' | '90d' | 'year';

const RANGES: { key: RangeKey; label: string }[] = [
  { key: '7d', label: 'Last 7 days' },
  { key: '30d', label: 'Last 30 days' },
  { key: '90d', label: 'Last 90 days' },
  { key: 'year', label: 'This year' },
];

const STATUSES: BookingSessionStatus[] = ['Active', 'Submitted', 'Abandoned', 'Cancelled'];

/**
 * Categorical, so the hues are deliberately distinct - but a confirmed booking
 * is a *success*, and this app's success colour is the accent. It was
 * `bg-green-100 text-green-700`, the one entry here with a token to use and the
 * only green in the product.
 */
const ACTIVITY_ICON: Record<string, string> = {
  BookingConfirmed: 'bg-accent-100 text-accent-700',
  BookingCancelled: 'bg-red-100 text-red-700',
  BookingRescheduled: 'bg-blue-100 text-blue-700',
  ReminderSent: 'bg-amber-100 text-amber-700',
  EmailSent: 'bg-gray-100 text-gray-600',
  EmailFailed: 'bg-red-100 text-red-700',
  BookingPageCreated: 'bg-violet-100 text-violet-700',
};

function toIsoDate(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** Range key -> inclusive from/to. Kept here so the label and the dates can never disagree. */
function rangeToDates(range: RangeKey): { from: string; to: string } {
  const today = new Date();
  const to = toIsoDate(today);
  if (range === 'year') return { from: `${today.getFullYear()}-01-01`, to };
  const days = range === '7d' ? 6 : range === '30d' ? 29 : 89;
  const start = new Date(today);
  start.setDate(start.getDate() - days);
  return { from: toIsoDate(start), to };
}

export function AnalyticsPage() {
  useDocumentTitle('Analytics');
  const { callProtected } = useAuth();

  const [range, setRange] = useState<RangeKey>('30d');
  const [pageId, setPageId] = useState<string>('');
  const [status, setStatus] = useState<string>('');
  const [pages, setPages] = useState<BookingPageSummaryDto[]>([]);

  const [bookings, setBookings] = useState<BookingAnalyticsDto | null>(null);
  const [funnel, setFunnel] = useState<ConversionFunnelDto | null>(null);
  const [operations, setOperations] = useState<OperationalAnalyticsDto | null>(null);
  const [activity, setActivity] = useState<ActivityEntryDto[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  // Which export is in flight, if any. A single value rather than two booleans:
  // one download at a time is the whole intent, and it makes "the other button
  // is disabled while this one runs" fall out rather than need coordinating.
  const [exporting, setExporting] = useState<AnalyticsExportFormat | null>(null);
  const [exportError, setExportError] = useState<string | null>(null);

  const filter = useMemo<AnalyticsFilter>(() => {
    const { from, to } = rangeToDates(range);
    return {
      from,
      to,
      bookingPageId: pageId || null,
      status: (status || null) as BookingSessionStatus | null,
    };
  }, [range, pageId, status]);

  useEffect(() => {
    callProtected((token) => api.organizer.getMyBookingPages(token))
      .then(setPages)
      .catch(() => setPages([]));
  }, [callProtected]);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      // One round trip per panel, issued together - the dashboard shows all of them,
      // and splitting keeps a slow calendar/activity read from blocking the rest.
      const [b, f, o, a] = await callProtected((token) =>
        Promise.all([
          api.analytics.getBookings(token, filter),
          api.analytics.getFunnel(token, filter),
          api.analytics.getOperations(token, filter),
          api.analytics.getActivity(token, filter, 20),
        ]),
      );
      setBookings(b);
      setFunnel(f);
      setOperations(o);
      setActivity(a);
    } catch (e) {
      setError(errorMessage(e, 'Failed to load analytics.'));
    } finally {
      setLoading(false);
    }
  }, [callProtected, filter]);

  useEffect(() => {
    void load();
  }, [load]);

  /**
   * Downloads the report under whatever filter the dashboard is currently
   * showing - the same `filter` object every panel above was rendered from, so
   * the file always matches what is on screen.
   */
  const exportReport = useCallback(async (format: AnalyticsExportFormat) => {
    setExporting(format);
    setExportError(null);
    try {
      const file = await callProtected((token) => (format === 'csv'
        ? api.analytics.exportCsv(token, filter)
        : api.analytics.exportPdf(token, filter)));
      saveFile(file);
    } catch (e) {
      // Reported separately from the dashboard's own error, so a failed export
      // never blanks out figures that loaded perfectly well.
      setExportError(errorMessage(e, `Failed to export the ${format.toUpperCase()} report.`));
    } finally {
      setExporting(null);
    }
  }, [callProtected, filter]);

  const summary = bookings?.summary;
  /**
   * Failed, with nothing to show. The panels below each fall back to their own
   * empty state ("No activity in this range yet", "No calendar connected"), so
   * leaving them up would answer the organizer's question with data the app
   * never received. Once a load has succeeded they stay through a later
   * failure - they are still true of the range they were fetched for.
   */
  const nothingLoaded = error !== null && bookings === null;

  return (
    <>
      <PageHeader
        title="Analytics"
        description="How your booking pages are performing, across everything you own."
      />

      {/* Filters in one row above the charts; every panel re-reads them together.
          A single hairline under the row groups them as this screen's toolbar
          without boxing each control - the same job the old borders did, once. */}
      <div className="mb-8 flex flex-wrap items-center gap-3 border-b border-gray-200 pb-5">
        {/* The group wraps below `sm` rather than squeezing: at 375px four
            buttons across one row leaves ~78px each, which broke every label
            onto two lines ("Last 7 / days"). `basis-1/2` makes that a tidy two
            rows of two - plain `grow` alone fits three on the first row and
            leaves a single 317px-wide button stranded on the second - and
            `sm:` puts it straight back to one row of natural widths.

            `grow` + `basis-1/2` rather than `flex-1`, deliberately: `flex-1` is
            the `flex` shorthand and sets `flex-basis: 0%`, so pairing it with a
            `basis-*` utility would leave the outcome to stylesheet order. These
            two set different properties and cannot fight. */}
        <div className="flex w-full flex-wrap rounded-lg border border-gray-200 p-1 sm:w-auto sm:flex-nowrap">
          {RANGES.map((r) => (
            <button
              key={r.key}
              type="button"
              onClick={() => setRange(r.key)}
              aria-pressed={range === r.key}
              className={`grow basis-1/2 whitespace-nowrap rounded-md px-3 py-1.5 text-sm font-medium transition-colors sm:grow-0 sm:basis-auto ${FOCUS_RING} ${
                range === r.key ? 'bg-accent-600 text-white' : 'text-gray-600 hover:bg-gray-100'
              }`}
            >
              {r.label}
            </button>
          ))}
        </div>

        {/* The two selects are one question - "which sessions?" - and stacked
            one per line they spent two rows of a phone screen saying it. Paired
            here so they share a row below `sm`; from `sm` the wrapper is
            auto-width around two natural-width selects with the same `gap-3`
            the bar itself uses, so the desktop row is unchanged. */}
        <div className="flex w-full gap-3 sm:w-auto">
          <select
            value={pageId}
            onChange={(e) => setPageId(e.target.value)}
            className={`${INPUT} min-w-0 flex-1 py-1.5 text-sm sm:flex-none`}
            aria-label="Filter by booking page"
          >
            <option value="">All booking pages</option>
            {pages.map((p) => <option key={p.id} value={p.id}>{p.title}</option>)}
          </select>

          <select
            value={status}
            onChange={(e) => setStatus(e.target.value)}
            className={`${INPUT} min-w-0 flex-1 py-1.5 text-sm sm:flex-none`}
            aria-label="Filter by status"
          >
            <option value="">All statuses</option>
            {STATUSES.map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </div>

        {/* Pushed to the far end of the same row as the filters, because what
            gets exported is exactly what those filters select. */}
        <div className="flex gap-2 sm:ml-auto">
          <ExportButton
            format="csv"
            label="Export CSV"
            icon={<FileSpreadsheet className="h-4 w-4" aria-hidden="true" />}
            exporting={exporting}
            onExport={exportReport}
          />
          <ExportButton
            format="pdf"
            label="Export PDF"
            icon={<FileText className="h-4 w-4" aria-hidden="true" />}
            exporting={exporting}
            onExport={exportReport}
          />
        </div>
      </div>

      {/*
        This screen always caught its failures - what it did with them was the
        problem. The panels below fall back to their own empty states, so a
        failed load rendered "No activity in this range yet", "No abandoned
        sessions in this range", "No calendar connected" - eight confident
        statements about the organizer's data, from a request that never
        returned. With nothing loaded the panels are therefore replaced
        outright; once figures are on screen a failed *filter change* keeps them
        and reports itself here, since they are still true of the range they
        were fetched for.
      */}
      {error && <LoadError message={error} onRetry={() => void load()} className="mb-4" />}

      {exportError && (
        <InlineNotice tone="error" className="mb-4">
          <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
          <span>{exportError}</span>
        </InlineNotice>
      )}

      {nothingLoaded ? null : (
      <>
      <div className="mb-6 grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-5">
        {loading && !summary ? (
          <SkeletonCards count={10} />
        ) : summary ? (
          <>
            <StatCard label="Visitors" value={summary.totalSessions} hint="Sessions started" />
            <StatCard label="Bookings" value={summary.confirmed} hint="Confirmed" tone="good" />
            <StatCard label="Completion rate" value={formatPercent(summary.completionRate)} hint="Visitors who booked" />
            <StatCard label="Upcoming" value={summary.upcoming} hint="Still to happen" />
            <StatCard label="Completed" value={summary.completed} hint="Already met" />
            <StatCard label="Cancelled" value={summary.cancelled} tone={summary.cancelled > 0 ? 'warning' : 'default'} />
            <StatCard label="Rescheduled" value={summary.rescheduled} hint="Moved at least once" />
            <StatCard label="Abandoned" value={summary.abandoned} hint="Left without booking" />
            <StatCard label="Booking pages" value={summary.totalBookingPages} hint={`${summary.activeBookingPages} active`} />
            <StatCard
              label="Avg. time to book"
              value={formatDuration(bookings?.completionTime.averageSeconds)}
              hint={bookings?.completionTime.sampleSize ? `${bookings.completionTime.sampleSize} bookings` : 'No data'}
            />
          </>
        ) : null}
      </div>

      <div className="mb-6 grid gap-4 lg:grid-cols-3">
        <div className="lg:col-span-2">
          {loading && !bookings ? <SkeletonPanel lines={6} /> : (
            <Panel title="Booking trend" subtitle="Sessions started vs bookings confirmed">
              {bookings && bookings.trend.length > 0
                ? <TrendChart points={bookings.trend} />
                : <EmptyState message="No activity in this range yet." />}
            </Panel>
          )}
        </div>

        {loading && !bookings ? <SkeletonPanel /> : (
          <Panel title="Status breakdown" subtitle="Mutually exclusive states">
            {bookings && bookings.statusDistribution.length > 0
              ? <StatusDoughnut slices={bookings.statusDistribution} />
              : <EmptyState message="Nothing to break down yet." />}
          </Panel>
        )}
      </div>

      <div className="mb-6 grid gap-4 lg:grid-cols-2">
        {loading && !funnel ? <SkeletonPanel lines={5} /> : (
          <Panel
            title="Conversion funnel"
            subtitle="From the booking-session event log"
            action={funnel && <span className="rounded-full bg-gray-100 px-2 py-0.5 text-xs font-medium text-gray-700">{formatPercent(funnel.conversionRate, 1)} overall</span>}
          >
            {funnel ? <FunnelChart steps={funnel.steps} /> : <EmptyState message="No funnel data yet." />}
          </Panel>
        )}

        {loading && !funnel ? <SkeletonPanel lines={5} /> : (
          <Panel title="Abandonment" subtitle="Where visitors drop off">
            {funnel && funnel.abandonment.totalAbandoned > 0 ? (
              <div className="space-y-4">
                <div className="grid grid-cols-3 gap-3 text-sm">
                  <div>
                    <p className="text-[13px] text-gray-500">Abandoned</p>
                    <p className="text-lg font-semibold text-gray-900">{funnel.abandonment.totalAbandoned}</p>
                  </div>
                  <div>
                    <p className="text-[13px] text-gray-500">Rate</p>
                    <p className="text-lg font-semibold text-gray-900">{formatPercent(funnel.abandonment.abandonmentRate)}</p>
                  </div>
                  <div>
                    <p className="text-[13px] text-gray-500">Avg. step</p>
                    <p className="text-lg font-semibold text-gray-900">
                      {funnel.abandonment.averageStepReached?.toFixed(1) ?? '—'}<span className="text-sm text-gray-500"> / {funnel.steps.length}</span>
                    </p>
                  </div>
                </div>
                {funnel.abandonment.mostCommonStep && (
                  <p className="text-sm text-gray-600">
                    Most common drop-off: <span className="font-medium text-gray-900">{funnel.abandonment.mostCommonStep}</span>
                  </p>
                )}
                <div className="space-y-2">
                  {funnel.abandonment.byStep.map((s) => (
                    <MeterRow key={s.step} label={s.step} value={s.share} caption={`${s.count} · ${(s.share * 100).toFixed(0)}%`} />
                  ))}
                </div>
              </div>
            ) : <EmptyState message="No abandoned sessions in this range." />}
          </Panel>
        )}
      </div>

      <div className="mb-6 grid gap-4 lg:grid-cols-2">
        {loading && !bookings ? <SkeletonPanel /> : (
          <Panel title="Popular weekdays" subtitle="Bookings by day of week">
            {bookings ? <WeekdayChart data={bookings.bookingsByWeekday} /> : null}
          </Panel>
        )}
        {loading && !bookings ? <SkeletonPanel /> : (
          <Panel title="Popular hours" subtitle="Bookings by start time">
            {bookings ? <HourChart data={bookings.bookingsByHour} timeZoneId={bookings.timeZoneId} /> : null}
          </Panel>
        )}
      </div>

      {bookings && bookings.pagePerformance.length > 1 && (
        <Panel title="Booking page performance" subtitle="Most active page first" className="mb-6">
          <div className="-mx-5 overflow-x-auto">
            <table className="w-full min-w-[42rem] text-sm">
              <thead>
                <tr className="border-b border-gray-200 text-left text-[13px] font-medium text-gray-500">
                  <th className="px-5 pb-3">Page</th>
                  <th className="px-3 pb-3 text-right">Views</th>
                  <th className="px-3 pb-3 text-right">Bookings</th>
                  <th className="px-3 pb-3 text-right">Conversion</th>
                  <th className="px-3 pb-3 text-right">Cancelled</th>
                  <th className="px-5 pb-3 text-right">Upcoming</th>
                </tr>
              </thead>
              <tbody>
                {bookings.pagePerformance.map((p) => (
                  <tr key={p.bookingPageId} className="border-b border-gray-50 last:border-0">
                    <td className="px-5 py-3">
                      <span className="font-medium text-gray-900">{p.title}</span>
                      {!p.isActive && <span className="ml-2 rounded bg-gray-100 px-1.5 py-0.5 text-xs text-gray-500">disabled</span>}
                    </td>
                    <td className="px-3 py-3 text-right tabular-nums text-gray-600">{p.views}</td>
                    <td className="px-3 py-3 text-right font-medium tabular-nums text-gray-900">{p.bookings}</td>
                    <td className="px-3 py-3 text-right tabular-nums text-gray-600">{formatPercent(p.conversionRate)}</td>
                    <td className="px-3 py-3 text-right tabular-nums text-gray-600">{p.cancelled}</td>
                    <td className="px-5 py-3 text-right tabular-nums text-gray-600">{p.upcoming}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Panel>
      )}

      <div className="mb-6 grid gap-4 lg:grid-cols-3">
        {loading && !operations ? <><SkeletonPanel /><SkeletonPanel /><SkeletonPanel /></> : operations ? (
          <>
            <Panel title="Email delivery" subtitle="Outbound notifications">
              <div className="space-y-3">
                <MeterRow label="Delivery rate" value={operations.email.deliveryRate} />
                <dl className="grid grid-cols-2 gap-2 text-sm">
                  <Stat label="Sent" value={operations.email.sent} />
                  <Stat label="Failed" value={operations.email.failed} />
                  <Stat label="Pending" value={operations.email.pending} />
                  <Stat label="Retries" value={operations.email.retries} />
                </dl>
                {operations.email.byType.length > 0 && (
                  <ul className="space-y-1 border-t border-gray-100 pt-2 text-xs">
                    {operations.email.byType.map((t) => (
                      <li key={t.notificationType} className="flex justify-between gap-2">
                        <span className="truncate text-gray-500">{t.notificationType}</span>
                        <span className="tabular-nums text-gray-700">{t.sent}/{t.total}</span>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            </Panel>

            <Panel title="Reminders" subtitle="Scheduled reminder outcomes">
              <dl className="grid grid-cols-2 gap-2 text-sm">
                <Stat label="Total" value={operations.reminders.total} />
                <Stat label="Sent" value={operations.reminders.sent} />
                <Stat label="Upcoming" value={operations.reminders.scheduled} />
                <Stat label="Cancelled" value={operations.reminders.cancelled} />
                <Stat label="Skipped" value={operations.reminders.skipped} />
                <Stat label="Failed" value={operations.reminders.failed} />
              </dl>
              <p className="mt-3 border-t border-gray-100 pt-2 text-xs text-gray-500">
                Average lead time:{' '}
                <span className="font-medium text-gray-900">
                  {operations.reminders.averageLeadTimeMinutes
                    ? formatDuration(operations.reminders.averageLeadTimeMinutes * 60)
                    : '—'}
                </span>
              </p>
            </Panel>

            <Panel title="Calendar sync" subtitle="Google Calendar">
              {operations.calendar.connected ? (
                <div className="space-y-3 text-sm">
                  <div className="flex items-center gap-2">
                    <CalendarCheck2 className="h-4 w-4 text-gray-400" aria-hidden="true" />
                    <span className="truncate text-gray-700">{operations.calendar.accountEmail}</span>
                  </div>
                  <MeterRow
                    label="Sync coverage"
                    value={operations.calendar.syncCoverage ?? 0}
                    caption={`${operations.calendar.syncedBookings}/${operations.calendar.confirmedBookings}`}
                  />
                  <dl className="space-y-1 text-xs">
                    <Row label="Status" value={operations.calendar.healthStatus ?? '—'} />
                    <Row label="Last success" value={operations.calendar.lastSuccessfulSyncAtUtc ? formatDateTime(operations.calendar.lastSuccessfulSyncAtUtc) : 'Never'} />
                    <Row label="Last failure" value={operations.calendar.lastFailedSyncAtUtc ? formatDateTime(operations.calendar.lastFailedSyncAtUtc) : 'Never'} />
                  </dl>
                  {operations.calendar.lastSyncError && (
                    <p className="text-xs text-red-600">{operations.calendar.lastSyncError}</p>
                  )}
                </div>
              ) : (
                <EmptyState message="No calendar connected. Connect Google Calendar to sync confirmed bookings." />
              )}
            </Panel>
          </>
        ) : null}
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <Panel title="Time to book" subtitle="Session start to confirmation" className="lg:col-span-1">
          {bookings && bookings.completionTime.sampleSize > 0 ? (
            <dl className="space-y-2 text-sm">
              <Row label="Average" value={formatDuration(bookings.completionTime.averageSeconds)} />
              <Row label="Median" value={formatDuration(bookings.completionTime.medianSeconds)} />
              <Row label="Fastest" value={formatDuration(bookings.completionTime.fastestSeconds)} />
              <Row label="Slowest" value={formatDuration(bookings.completionTime.slowestSeconds)} />
              <Row label="Sample" value={`${bookings.completionTime.sampleSize} bookings`} />
            </dl>
          ) : <EmptyState message="No completed bookings in this range." />}
        </Panel>

        <Panel title="Recent activity" subtitle="Across your booking pages" className="lg:col-span-2">
          {activity && activity.length > 0 ? (
            <ul className="space-y-3.5">
              {activity.map((entry, i) => (
                <li key={`${entry.occurredAtUtc}-${i}`} className="flex items-start gap-3">
                  <span className={`mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-full ${ACTIVITY_ICON[entry.kind] ?? 'bg-gray-100 text-gray-600'}`}>
                    <ActivityGlyph kind={entry.kind} />
                  </span>
                  <span className="min-w-0 flex-1">
                    <span className="block truncate text-[15px] text-gray-800">{entry.description}</span>
                    <span className="text-[13px] text-gray-500">{formatRelative(entry.occurredAtUtc)}</span>
                  </span>
                </li>
              ))}
            </ul>
          ) : <EmptyState message="No recent activity in this range." />}
        </Panel>
      </div>
      </>
      )}
    </>
  );
}

/**
 * One export button. Shows a spinner in place of its icon while its own download
 * is running, and both buttons disable while either is running - a second
 * request would only produce a second copy of the same file.
 */
function ExportButton({
  format,
  label,
  icon,
  exporting,
  onExport,
}: {
  format: AnalyticsExportFormat;
  label: string;
  icon: ReactNode;
  exporting: AnalyticsExportFormat | null;
  onExport: (format: AnalyticsExportFormat) => void;
}) {
  const isThisOne = exporting === format;
  return (
    <button
      type="button"
      onClick={() => onExport(format)}
      disabled={exporting !== null}
      aria-busy={isThisOne}
      className={BUTTON_SECONDARY}
    >
      {isThisOne
        ? <Download className="h-4 w-4 animate-bounce" aria-hidden="true" />
        : icon}
      {isThisOne ? 'Preparing…' : label}
    </button>
  );
}

function Stat({ label, value }: { label: string; value: number }) {
  return (
    <div>
      <dt className="text-[13px] text-gray-500">{label}</dt>
      <dd className="text-base font-semibold tabular-nums text-gray-900">{value}</dd>
    </div>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex justify-between gap-2">
      <dt className="text-gray-500">{label}</dt>
      <dd className="truncate text-gray-900">{value}</dd>
    </div>
  );
}

/**
 * The coloured glyph beside an activity row. Purely decorative: it categorises
 * `entry.description`, a full sentence sitting right next to it. See
 * `AppSidebar.ItemIcon` for why `aria-hidden` is spelled out rather than left
 * to lucide's own default.
 */
function ActivityGlyph({ kind }: { kind: string }) {
  const cls = 'h-4 w-4';
  if (kind === 'ReminderSent') return <Clock className={cls} aria-hidden="true" />;
  if (kind === 'EmailSent' || kind === 'EmailFailed') return <Mail className={cls} aria-hidden="true" />;
  if (kind === 'BookingPageCreated') return <BarChart3 className={cls} aria-hidden="true" />;
  if (kind === 'BookingRescheduled') return <TrendingUp className={cls} aria-hidden="true" />;
  if (kind === 'BookingCancelled') return <TriangleAlert className={cls} aria-hidden="true" />;
  return <Activity className={cls} aria-hidden="true" />;
}
