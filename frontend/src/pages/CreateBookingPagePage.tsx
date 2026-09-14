import { useEffect, useId, useState } from 'react';
import type { FormEvent, ReactNode } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { AlertCircle, CheckCircle2, ChevronRight, Clock3 } from 'lucide-react';
import {
  BUTTON_PRIMARY, BUTTON_SECONDARY, CARD, FOCUS_RING, INPUT, InlineNotice, LABEL, META, PageHeader,
  Section,
} from '../components/ui';
import { useAuth } from '../contexts/AuthContext';
import { api, errorMessage } from '../lib/api';
import { hasBookableAvailability } from '../lib/availability';
import { useDocumentTitle } from '../lib/pageTitle';

const labelClass = LABEL;
const helperClass = `mt-1 ${META}`;
// No width utility baked in here deliberately - Tailwind resolves conflicting
// width classes by stylesheet order, not by position in the class attribute,
// so callers that need a fixed width (e.g. `${inputClass} w-32`) couldn't
// reliably override a shared `w-full`. Each usage below sets its own width.
const inputClass = INPUT;
// The app's one optional marker, matching `FieldLabel`'s. Spelled out here
// rather than using that primitive because each label on this screen is
// followed by a helper line carrying its own `mt-1`, so the shared label gap
// would double the space - see LABEL_GAP.
const optionalTag = <span className="font-normal text-gray-500"> (optional)</span>;

function toOptionalInt(value: string): number | null {
  if (value.trim() === '') return null;
  const parsed = Number(value);
  return Number.isNaN(parsed) ? null : parsed;
}

/** Approximates the backend's slug generation purely for display - the server is always the source of truth for the real slug. */
function previewSlug(title: string): string {
  const slug = title
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
  return slug || 'booking-page';
}

interface SectionProps {
  title: string;
  description: string;
  children: ReactNode;
}

/**
 * This screen used to carry its own section treatment - a 32px icon chip, a
 * larger heading and an 11-unit indent - so the one page an organizer sees
 * first looked like a different product from the rest. It renders through the
 * app's Section now; the icons are gone with it, because a decorative glyph
 * beside a heading is the definition of ornament.
 */
function FormSection({ title, description, children }: SectionProps) {
  return (
    <Section title={title} description={description}>
      <div className="space-y-4">{children}</div>
    </Section>
  );
}

interface PresetOption {
  value: number;
  label: string;
}

interface PresetNumberFieldProps {
  label: string;
  helperText: string;
  value: number;
  onChange: (value: number) => void;
  options: PresetOption[];
  unit: string;
  min?: number;
}

/** A dropdown of common presets that reveals a plain number input only when "Custom..." is chosen. */
function PresetNumberField({ label, helperText, value, onChange, options, unit, min = 0 }: PresetNumberFieldProps) {
  const id = useId();
  const [isCustom, setIsCustom] = useState(() => !options.some((o) => o.value === value));

  return (
    <div>
      <label className={labelClass} htmlFor={id}>{label}</label>
      <p className={helperClass} id={`${id}-helper`}>{helperText}</p>
      <select
        id={id}
        aria-describedby={`${id}-helper`}
        className={`${inputClass} mt-2 w-full`}
        value={isCustom ? 'custom' : String(value)}
        onChange={(e) => {
          if (e.target.value === 'custom') {
            setIsCustom(true);
          } else {
            setIsCustom(false);
            onChange(Number(e.target.value));
          }
        }}
      >
        {options.map((o) => (
          <option key={o.value} value={o.value}>{o.label}</option>
        ))}
        <option value="custom">Custom&hellip;</option>
      </select>

      {isCustom && (
        <div className="mt-2 flex items-center gap-2">
          <input
            type="number"
            min={min}
            className={`${inputClass} w-28`}
            value={value}
            onChange={(e) => onChange(Number(e.target.value))}
            aria-label={`Custom ${label.toLowerCase()} in ${unit}`}
          />
          <span className="text-sm text-gray-500">{unit}</span>
        </div>
      )}
    </div>
  );
}

interface OptionalNumberFieldProps {
  label: string;
  helperText: string;
  value: string;
  onChange: (value: string) => void;
  unit: string;
  min?: number;
  placeholder: string;
}

function OptionalNumberField({ label, helperText, value, onChange, unit, min, placeholder }: OptionalNumberFieldProps) {
  const id = useId();
  return (
    <div>
      <label className={labelClass} htmlFor={id}>
        {label}
        {optionalTag}
      </label>
      <p className={helperClass} id={`${id}-helper`}>{helperText}</p>
      <div className="mt-2 flex items-center gap-2">
        <input
          id={id}
          aria-describedby={`${id}-helper`}
          type="number"
          min={min}
          className={`${inputClass} w-32`}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder={placeholder}
        />
        <span className="text-sm text-gray-500">{unit}</span>
      </div>
    </div>
  );
}

const DURATION_OPTIONS: PresetOption[] = [
  { value: 15, label: '15 minutes' },
  { value: 30, label: '30 minutes' },
  { value: 45, label: '45 minutes' },
  { value: 60, label: '60 minutes' },
  { value: 90, label: '90 minutes' },
  { value: 120, label: '120 minutes' },
];

const BUFFER_OPTIONS: PresetOption[] = [
  { value: 0, label: 'None' },
  { value: 5, label: '5 minutes' },
  { value: 10, label: '10 minutes' },
  { value: 15, label: '15 minutes' },
  { value: 30, label: '30 minutes' },
];

/** First-time-organizer onboarding lands here automatically, and it's also reachable any time via "New booking page". */
export function CreateBookingPagePage() {
  useDocumentTitle('New booking page');
  const { callProtected, organizer } = useAuth();
  const navigate = useNavigate();

  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [duration, setDuration] = useState(30);
  const [bufferBefore, setBufferBefore] = useState(0);
  const [bufferAfter, setBufferAfter] = useState(0);
  const [minNotice, setMinNotice] = useState('');
  const [maxWindow, setMaxWindow] = useState('');
  const [maxPerDay, setMaxPerDay] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [scheduleChecked, setScheduleChecked] = useState(false);
  const [hasSchedule, setHasSchedule] = useState(false);
  const [scheduleUnknown, setScheduleUnknown] = useState(false);
  const browserTimeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;

  // A failure here must not block creating a page - the form is perfectly
  // usable without knowing the answer, and the server decides whether to seed a
  // schedule regardless. But "Checking your availability..." spun for ever
  // without a `catch`, so the one section that promises an outcome never
  // delivered one.
  useEffect(() => {
    callProtected((token) => api.availability.getSchedule(token))
      .then((schedule) => setHasSchedule(hasBookableAvailability(schedule)))
      .catch(() => setScheduleUnknown(true))
      .finally(() => setScheduleChecked(true));
  }, [callProtected]);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      const page = await callProtected((token) =>
        api.organizer.createBookingPage(token, {
          title,
          description: description || null,
          durationMinutes: duration,
          bufferBeforeMinutes: bufferBefore,
          bufferAfterMinutes: bufferAfter,
          minNoticeMinutes: toOptionalInt(minNotice),
          maxBookingWindowDays: toOptionalInt(maxWindow),
          maxBookingsPerDay: toOptionalInt(maxPerDay),
          // Only ever used to decide which clock a FIRST working schedule is
          // created on, server-side. An organizer who already has one is
          // unaffected by it - see CreateBookingPageCommand.
          timeZoneId: browserTimeZone,
        }),
      );
      // Always the page's own dashboard, never a settings screen: with defaults
      // the page is bookable the moment it exists, and NewBookingPagePanel
      // there carries both outcomes - the public link when it is live, and the
      // route to Working hours when the organizer has closed every day.
      navigate(`/dashboard/${page.id}`, { state: { justCreatedPage: true, createdSlug: page.slug } });
    } catch (err) {
      // Through `errorMessage`, so a rejected title reaches the organizer and
      // an unreachable API is not reported as a problem with "the fields above".
      setError(errorMessage(err, 'Failed to create booking page. Check the fields above and try again.'));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <>
      <PageHeader
        title="Create a booking page"
        description="Set up a new event type that guests can find and book with you."
      />

      <div className="grid grid-cols-1 gap-8 lg:grid-cols-[minmax(0,1fr)_280px]">
          <div className="max-w-xl">
            <form onSubmit={handleSubmit} className="space-y-7">
              <FormSection title="Basic Information" description="What guests will see before they book.">
                <div>
                  <label className={labelClass} htmlFor="title">Title</label>
                  <input
                    id="title"
                    className={`${inputClass} mt-2 w-full`}
                    value={title}
                    onChange={(e) => setTitle(e.target.value)}
                    placeholder="e.g. 30 Minute Meeting"
                    required
                  />
                  <p className={helperClass}>
                    This becomes part of your public booking link:{' '}
                    <span className="font-mono text-gray-600">.../book/{previewSlug(title)}</span>
                  </p>
                </div>

                <div>
                  <label className={labelClass} htmlFor="description">
                    Description
                    {optionalTag}
                  </label>
                  <p className={helperClass} id="description-helper">This will be shown to guests before they book.</p>
                  <textarea
                    id="description"
                    aria-describedby="description-helper"
                    className={`${inputClass} mt-2 w-full`}
                    rows={3}
                    value={description}
                    onChange={(e) => setDescription(e.target.value)}
                    placeholder="What should guests know about this meeting?"
                  />
                </div>
              </FormSection>

              <div className="border-t border-gray-100" />

              <FormSection title="Meeting Settings" description="How long meetings last and the breathing room around them.">
                <div className="grid grid-cols-1 gap-5 sm:grid-cols-3">
                  <PresetNumberField
                    label="Duration"
                    helperText="How long each meeting lasts."
                    value={duration}
                    onChange={setDuration}
                    options={DURATION_OPTIONS}
                    unit="minutes"
                    min={1}
                  />
                  <PresetNumberField
                    label="Buffer before"
                    helperText="Extra free time before each meeting."
                    value={bufferBefore}
                    onChange={setBufferBefore}
                    options={BUFFER_OPTIONS}
                    unit="minutes"
                  />
                  <PresetNumberField
                    label="Buffer after"
                    helperText="Extra free time after each meeting."
                    value={bufferAfter}
                    onChange={setBufferAfter}
                    options={BUFFER_OPTIONS}
                    unit="minutes"
                  />
                </div>
              </FormSection>

              <div className="border-t border-gray-100" />

              <FormSection title="Availability" description="When guests are able to book this page.">
                {!scheduleChecked && (
                  <div className="flex items-center gap-3 rounded-lg bg-gray-50 px-4 py-3">
                    <span className="h-4 w-4 shrink-0 animate-pulse rounded-full bg-gray-300" />
                    <p className="text-sm text-gray-500">Checking your availability&hellip;</p>
                  </div>
                )}
                {scheduleChecked && scheduleUnknown && (
                  // Says which claim it cannot make, rather than making the
                  // wrong one - both of the panels below assert something
                  // specific about hours that will or will not exist.
                  <InlineNotice tone="warning">
                    We could not check your existing availability. Creating the page still works, and
                    you can set your hours from Working hours afterwards.
                  </InlineNotice>
                )}
                {scheduleChecked && !scheduleUnknown && hasSchedule && (
                  // Was the only emerald in the product. Its sibling branch
                  // below renders in the same slot, in the same shape, on the
                  // accent - and the accent is this app's success colour.
                  <div className="flex items-start gap-3 rounded-lg bg-accent-50 px-4 py-3">
                    <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-accent-600" aria-hidden="true" />
                    <p className="text-sm text-accent-900">
                      This booking page uses your default working schedule.
                      <span className="block text-accent-800">
                        You can fine-tune your availability anytime from Working Hours.
                      </span>
                    </p>
                  </div>
                )}
                {scheduleChecked && !scheduleUnknown && !hasSchedule && (
                  // Not a warning any more: creating this page also creates the
                  // schedule, so the honest thing to state is what those hours
                  // will be rather than what is missing.
                  <div className="flex items-start gap-3 rounded-lg bg-accent-50 px-4 py-3">
                    <Clock3 className="mt-0.5 h-4 w-4 shrink-0 text-accent-600" aria-hidden="true" />
                    <p className="text-sm text-accent-900">
                      We&rsquo;ll start you on Monday&ndash;Friday, 09:00&ndash;17:00 ({browserTimeZone}).
                      <span className="block text-accent-800">
                        Change it whenever you like from Working hours &mdash; your schedule is shared by
                        every booking page you own.
                      </span>
                    </p>
                  </div>
                )}
              </FormSection>

              <div className="border-t border-gray-100" />

              {/*
                Collapsed by default, and this is the one piece of real friction
                the create form had: three questions a first-time organizer has
                no basis to answer, every one of them optional, sitting between
                them and the button. Native <details> rather than a toggle in
                state - it is a disclosure, the browser already implements one,
                and the fields stay in the form and submit either way.
              */}
              <details className="group">
                <summary
                  className={`inline-flex cursor-pointer list-none items-center gap-1.5 rounded text-sm font-medium text-gray-700 hover:text-gray-900 [&::-webkit-details-marker]:hidden ${FOCUS_RING}`}
                >
                  <ChevronRight
                    className="h-4 w-4 transition-transform group-open:rotate-90"
                    aria-hidden="true"
                  />
                  Booking limits
                  <span className="font-normal text-gray-500">— optional, unlimited by default</span>
                </summary>

                <div className="mt-4 space-y-4">
                  <p className={helperClass}>Guardrails on when and how often guests can book.</p>
                  <div className="grid grid-cols-1 gap-5 sm:grid-cols-3">
                  <OptionalNumberField
                    label="Minimum notice"
                    helperText="How long before a meeting guests are allowed to book."
                    value={minNotice}
                    onChange={setMinNotice}
                    unit="minutes"
                    min={0}
                    placeholder="No minimum"
                  />
                  <OptionalNumberField
                    label="Booking window"
                    helperText="How many days into the future guests can book."
                    value={maxWindow}
                    onChange={setMaxWindow}
                    unit="days"
                    min={1}
                    placeholder="Unlimited"
                  />
                  <OptionalNumberField
                    label="Max per day"
                    helperText="Cap how many meetings can be booked on a single day."
                    value={maxPerDay}
                    onChange={setMaxPerDay}
                    unit="bookings"
                    min={1}
                    placeholder="Unlimited"
                  />
                  </div>
                </div>
              </details>

              {error && (
                <InlineNotice tone="error">
                  <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
                  <span>{error}</span>
                </InlineNotice>
              )}

              {/* Primary action first in reading order and visually dominant;
                  Cancel beside it as a peer rather than right-aligned away from
                  the form it belongs to. */}
              <div className="flex items-center gap-2 border-t border-gray-200 pt-4">
                <button type="submit" disabled={submitting} className={BUTTON_PRIMARY}>
                  {submitting ? 'Creating…' : 'Create booking page'}
                </button>
                <Link to="/dashboard" className={BUTTON_SECONDARY}>Cancel</Link>
              </div>
            </form>
          </div>

          <aside className="hidden lg:block">
            <div className="sticky top-0">
              <p className="mb-2 text-[11px] font-medium uppercase tracking-wide text-gray-500">Preview</p>
              <div className={`${CARD} p-4`}>
                {/* Accent, not gray-900: this claims to show what a guest sees,
                    and the guest's booking sheet has been on the accent since
                    the wizard was brought into the design system. A preview
                    drawn in the previous palette is a preview of a screen that
                    no longer exists. */}
                <div className="mb-3 flex h-8 w-8 items-center justify-center rounded-full bg-accent-600 text-xs font-medium text-white">
                  {(organizer?.name ?? 'O').charAt(0).toUpperCase()}
                </div>
                <p className="text-[13px] text-gray-500">{organizer?.name ?? 'Your name'}</p>
                <h3 className="mt-1 text-lg font-semibold text-gray-900">
                  {title.trim() || 'Your event title'}
                </h3>

                <div className="mt-2 flex flex-wrap items-center gap-x-2 gap-y-1 text-sm text-gray-500">
                  <span className="inline-flex items-center gap-1 rounded-full bg-gray-100 px-2.5 py-1 font-medium text-gray-700">
                    <Clock3 className="h-3.5 w-3.5" aria-hidden="true" />
                    {duration} min
                  </span>
                  {(bufferBefore > 0 || bufferAfter > 0) && (
                    <span className="text-xs text-gray-500">
                      +{bufferBefore + bufferAfter} min buffer
                    </span>
                  )}
                </div>

                <p className="mt-4 text-sm text-gray-600">
                  {description.trim() || <span className="text-gray-500">No description provided</span>}
                </p>

                <div className="mt-5 rounded-lg border border-dashed border-gray-200 p-4 text-center text-xs text-gray-500">
                  Guests will see your available times here
                </div>

                <div
                  aria-hidden="true"
                  className="mt-4 w-full rounded-lg bg-accent-600 px-4 py-2.5 text-center text-sm font-medium text-white opacity-90"
                >
                  Book a time
                </div>
              </div>
            </div>
          </aside>
      </div>
    </>
  );
}
