import { API_BASE_URL } from './config';
import type {
  ActivityEntryDto,
  AnalyticsFilter,
  ApiProblem,
  AuthResultDto,
  BookingAnalyticsDto,
  ConversionFunnelDto,
  OperationalAnalyticsDto,
  AvailabilityExceptionDto,
  AvailabilityOverrideDto,
  AvailableSlotDto,
  BookingConfirmationDto,
  BookingFieldType,
  BookingPageDetailDto,
  BookingPageDto,
  BookingPageSummaryDto,
  BookingSessionDto,
  BookingReminderDto,
  BookingSessionEventDto,
  BookingSessionStatus,
  CalendarConnectionDto,
  ClientBookingEventDto,
  CreateBookingPageRequest,
  DownloadedFile,
  ExternalCalendarDto,
  MeetingProvider,
  NotificationSettingsDto,
  PublicBookingDto,
  TimeRangeDto,
  WorkingDayDto,
  WorkingScheduleDto,
} from './types';

/** The server responded and rejected the request - `problem` is its own `{ title, errors? }` body. */
export class ApiError extends Error {
  status: number;
  problem: ApiProblem;

  constructor(status: number, problem: ApiProblem) {
    super(problem.title);
    this.status = status;
    this.problem = problem;
  }
}

/**
 * The request never reached the API at all - the server is down, VITE_API_BASE_URL
 * points somewhere wrong, or CORS/DNS failed. Deliberately a separate type from
 * ApiError (which means the server DID answer, with a rejection): without the
 * split, every call site's `instanceof ApiError ? ... : 'Failed to X.'` fallback
 * reports an unreachable server using its own domain wording ("Failed to
 * register.", "Failed to cancel this booking."), which points debugging at the
 * wrong layer entirely. Use `errorMessage()` rather than testing for this by hand.
 */
export class NetworkError extends Error {
  constructor(cause?: unknown) {
    super('Could not reach the server.', { cause });
    this.name = 'NetworkError';
  }
}

const NETWORK_MESSAGE = 'Could not reach the server. Please check your connection and try again.';

/**
 * The field-keyed messages a rejected request carried, if any.
 *
 * `ExceptionHandlingMiddleware` answers a `ValidationException` with
 * `{ title: "Validation failed", errors: { PropertyName: [message, ...] } }`.
 * The title is a category, never a sentence worth showing anyone - every actual
 * explanation the backend writes ("'Europe/Skopj' is not a recognized IANA time
 * zone id.", "Intervals within a single day must not overlap.") lives in
 * `errors`, and until this existed none of them ever reached a screen.
 *
 * Three key shapes, all confirmed against the running API rather than assumed:
 *   - FluentValidation's `PropertyName`, verbatim: `Events`,
 *     `Events[0].NewValue`, `TimeZoneId`. `Program.cs` sets
 *     `PropertyNamingPolicy` but not `DictionaryKeyPolicy`, so these stay
 *     PascalCase - camelCase applies to DTO properties, never to dictionary keys.
 *   - `""` for a rule written against the request root (`RuleFor(x => x)`), and
 *     `$` from ASP.NET's own model-binding `ProblemDetails`, which is a second
 *     producer of this same `errors` shape with a different title. Neither names
 *     a field, so both land in `unmapped`.
 *   - `custom:{fieldId}` from `SubmitBookingSessionCommandHandler` - the same
 *     string `customFieldName` builds - so the booking wizard can point at the
 *     question that is missing.
 *
 * Lookup is case-insensitive so a screen keeps working if `DictionaryKeyPolicy`
 * is ever set, rather than silently losing every field message.
 */
export interface ValidationErrors {
  /** True when the server supplied any field-keyed messages at all. */
  readonly hasAny: boolean;
  /** Messages for one field key. Empty when the server said nothing about it. */
  for(field: string): string[];
  /**
   * Everything whose key matched none of `mappedFields` - what a form must show
   * at form level, because no input on screen corresponds to it. Callers pass
   * the keys they actually render beside a field, so a message is never both
   * shown next to an input and repeated in the summary, and never silently
   * dropped because nothing claimed it.
   */
  unmapped(mappedFields: string[]): string[];
}

/** The "nothing was rejected" value, so callers can hold one without a null check. */
export const NO_VALIDATION_ERRORS: ValidationErrors = {
  hasAny: false,
  for: () => [],
  unmapped: () => [],
};

export function validationErrors(error: unknown): ValidationErrors {
  const raw = error instanceof ApiError ? error.problem.errors : undefined;
  if (!raw) return NO_VALIDATION_ERRORS;

  const byField = new Map<string, string[]>();
  for (const [key, messages] of Object.entries(raw)) {
    if (!Array.isArray(messages)) continue;
    const clean = messages.filter((m) => typeof m === 'string' && m.trim() !== '');
    if (clean.length === 0) continue;
    const normalized = key.toLowerCase();
    byField.set(normalized, [...(byField.get(normalized) ?? []), ...clean]);
  }

  if (byField.size === 0) return NO_VALIDATION_ERRORS;

  return {
    hasAny: true,
    for: (field) => byField.get(field.toLowerCase()) ?? [],
    unmapped: (mappedFields) => {
      const claimed = new Set(mappedFields.map((f) => f.toLowerCase()));
      return dedupe([...byField].filter(([key]) => !claimed.has(key)).flatMap(([, messages]) => messages));
    },
  };
}

/** Distinct, order-preserving. One rule broken across a batch repeats its message per item. */
function dedupe(messages: string[]): string[] {
  return [...new Set(messages)];
}

/**
 * Resolves the message to show the user for anything thrown by this client.
 * `fallback` covers only genuinely unexpected errors (a real bug) - a rejected
 * request uses the server's own message, and an unreachable server says so
 * instead of borrowing the caller's domain wording. `networkMessage` overrides
 * the connectivity text for organizer-facing screens, where naming the API is
 * useful rather than confusing.
 *
 * A validation rejection resolves to the server's own field messages rather
 * than its title, because the title in that one case is the literal string
 * "Validation failed" - which tells a user something is wrong and nothing about
 * what. Screens that place messages beside their inputs should use
 * `validationErrors` instead; this is what every other call site gets for free.
 */
export function errorMessage(error: unknown, fallback: string, networkMessage: string = NETWORK_MESSAGE): string {
  if (error instanceof ApiError) {
    const validation = validationErrors(error);
    if (validation.hasAny) return validation.unmapped([]).join(' ');
    return error.problem.title;
  }
  if (error instanceof NetworkError) return networkMessage;
  return fallback;
}

/**
 * Performs the request and resolves to the raw Response, having already turned
 * both failure modes into this module's two error types. Everything that talks
 * to the API goes through here, so a JSON call and a file download report an
 * unreachable server and a rejected request identically.
 */
async function fetchOrThrow(path: string, init?: RequestInit): Promise<Response> {
  // init spread AFTER headers would normally clobber the Content-Type default
  // whenever a caller (e.g. authorizedRequest) supplies its own headers object -
  // object spread replaces whole keys, it doesn't merge nested objects - so
  // headers are merged explicitly here instead of relying on the outer spread.
  let response: Response;
  try {
    response = await fetch(`${API_BASE_URL}${path}`, {
      ...init,
      headers: { 'Content-Type': 'application/json', ...init?.headers },
    });
  } catch (e) {
    // fetch() rejects only when the request never completed - a non-2xx response
    // resolves normally and is handled below.
    throw new NetworkError(e);
  }

  if (!response.ok) {
    // Error bodies are always the `{ title }` JSON shape, even on an endpoint
    // whose success response is a file.
    const problem: ApiProblem = await response
      .json()
      .catch(() => ({ title: response.statusText }));
    throw new ApiError(response.status, problem);
  }

  return response;
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetchOrThrow(path, init);
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

/**
 * A binary download rather than JSON. The file name comes from the server's
 * Content-Disposition header, so the browser saves the name the export chose
 * (which encodes the filter) rather than one invented on the client.
 */
async function fileRequest(path: string, init: RequestInit, fallbackFileName: string): Promise<DownloadedFile> {
  const response = await fetchOrThrow(path, init);
  return {
    blob: await response.blob(),
    fileName: fileNameFromContentDisposition(response.headers.get('Content-Disposition')) ?? fallbackFileName,
  };
}

/**
 * Reads the file name out of a Content-Disposition header, preferring the
 * RFC 5987 `filename*` form when present because that is the one that survives
 * non-ASCII. Returns null if the header is absent - which is what happens when
 * the API forgets to expose the header through CORS, so callers must have a
 * fallback rather than downloading a file called "undefined".
 */
function fileNameFromContentDisposition(header: string | null): string | null {
  if (!header) return null;

  const extended = /filename\*=UTF-8''([^;]+)/i.exec(header);
  if (extended) {
    try {
      return decodeURIComponent(extended[1].trim());
    } catch {
      // A malformed percent-encoding is not worth failing a download over.
    }
  }

  const plain = /filename="?([^";]+)"?/i.exec(header);
  return plain ? plain[1].trim() : null;
}

/** Same as request(), plus a Bearer token - used for every organizer-only endpoint. */
function authorizedRequest<T>(accessToken: string, path: string, init?: RequestInit): Promise<T> {
  return request<T>(path, {
    ...init,
    headers: { Authorization: `Bearer ${accessToken}` },
  });
}

/** Serializes an analytics filter, omitting unset values so the backend sees an absent param rather than an empty string. */
function analyticsQuery(filter: AnalyticsFilter, extra?: Record<string, string>): string {
  const params = new URLSearchParams();
  if (filter.from) params.set('from', filter.from);
  if (filter.to) params.set('to', filter.to);
  if (filter.bookingPageId) params.set('bookingPageId', filter.bookingPageId);
  if (filter.status) params.set('status', filter.status);
  for (const [key, value] of Object.entries(extra ?? {})) params.set(key, value);
  const query = params.toString();
  return query ? `?${query}` : '';
}

export const api = {
  getBookingPage: (slug: string) => request<BookingPageDto>(`/api/booking-pages/${slug}`),

  startSession: (slug: string) =>
    request<BookingSessionDto>(`/api/booking-pages/${slug}/sessions`, { method: 'POST' }),

  appendEvents: (sessionId: string, events: ClientBookingEventDto[]) =>
    request<BookingSessionDto>(`/api/booking-sessions/${sessionId}/events`, {
      method: 'POST',
      body: JSON.stringify(events),
    }),

  submitSession: (sessionId: string, clientSequenceNumber: number) =>
    request<BookingConfirmationDto>(`/api/booking-sessions/${sessionId}/submit`, {
      method: 'POST',
      body: JSON.stringify({ clientSequenceNumber }),
    }),

  /**
   * Anonymous, and the one anonymous call that returns the guest's own details:
   * useBookingSessionTracker re-reads the session on mount to restore a
   * half-filled form after a reload. There is no credential to present at that
   * point - the PublicToken does not exist until submit.
   *
   * There is deliberately no anonymous `getTimeline` beside this. It existed,
   * had no caller, and returned every keystroke plus the client IP and
   * User-Agent; the timeline the app actually renders is
   * `api.organizer.getTimeline`, which is authenticated and ownership-checked.
   */
  getSession: (sessionId: string) => request<BookingSessionDto>(`/api/booking-sessions/${sessionId}`),

  /** The event-sourcing proof endpoint - returns the same shape as getSession, replayed from the log. */
  rebuildSession: (sessionId: string) =>
    request<BookingSessionDto>(`/api/booking-sessions/${sessionId}/rebuild`),

  /** Public - the only availability information a visitor can query. */
  getAvailableSlots: (slug: string, from: string, to: string) =>
    request<AvailableSlotDto[]>(`/api/booking-pages/${slug}/slots?from=${from}&to=${to}`),

  /**
   * navigator.sendBeacon can't set headers or await a response - it's fire-and-forget,
   * which is exactly what's needed when the page is unloading. Returns whether the
   * browser accepted the beacon (not whether the server processed it).
   */
  sendBeacon: (sessionId: string, events: ClientBookingEventDto[]): boolean => {
    const blob = new Blob([JSON.stringify(events)], { type: 'application/json' });
    return navigator.sendBeacon(`${API_BASE_URL}/api/booking-sessions/${sessionId}/events/beacon`, blob);
  },

  /** Public - the PublicToken is the sole credential, no auth header involved. */
  bookings: {
    getByToken: (token: string) => request<PublicBookingDto>(`/api/bookings/${token}`),

    cancel: (token: string, reason?: string) =>
      request<BookingConfirmationDto>(`/api/bookings/${token}/cancel`, {
        method: 'POST',
        body: JSON.stringify({ reason: reason ?? null }),
      }),

    reschedule: (token: string, newDate: string, newTime: string) =>
      request<BookingConfirmationDto>(`/api/bookings/${token}/reschedule`, {
        method: 'POST',
        body: JSON.stringify({ newDate, newTime }),
      }),
  },

  /** Public - issues the tokens every organizer-only endpoint below requires. */
  auth: {
    register: (name: string, email: string, password: string) =>
      request<AuthResultDto>('/api/auth/register', { method: 'POST', body: JSON.stringify({ name, email, password }) }),

    login: (email: string, password: string) =>
      request<AuthResultDto>('/api/auth/login', { method: 'POST', body: JSON.stringify({ email, password }) }),

    refresh: (refreshToken: string) =>
      request<AuthResultDto>('/api/auth/refresh', { method: 'POST', body: JSON.stringify({ refreshToken }) }),

    logout: (refreshToken: string) =>
      request<void>('/api/auth/logout', { method: 'POST', body: JSON.stringify({ refreshToken }) }),
  },

  /** Organizer-only - every call is ownership-checked server-side against the token's organizer id. */
  organizer: {
    getMyBookingPages: (accessToken: string) =>
      authorizedRequest<BookingPageSummaryDto[]>(accessToken, '/api/organizer/booking-pages'),

    createBookingPage: (accessToken: string, payload: CreateBookingPageRequest) =>
      authorizedRequest<BookingPageDetailDto>(accessToken, '/api/organizer/booking-pages', {
        method: 'POST',
        body: JSON.stringify(payload),
      }),

    getBookingPage: (accessToken: string, pageId: string) =>
      authorizedRequest<BookingPageDetailDto>(accessToken, `/api/organizer/booking-pages/${pageId}`),

    updateBookingPageDetails: (accessToken: string, pageId: string, title: string, description: string | null) =>
      authorizedRequest<BookingPageDetailDto>(accessToken, `/api/organizer/booking-pages/${pageId}/details`, {
        method: 'PUT',
        body: JSON.stringify({ title, description }),
      }),

    updateBookingPageLimits: (
      accessToken: string,
      pageId: string,
      minNoticeMinutes: number | null,
      maxBookingWindowDays: number | null,
      maxBookingsPerDay: number | null,
    ) =>
      authorizedRequest<BookingPageDetailDto>(accessToken, `/api/organizer/booking-pages/${pageId}/limits`, {
        method: 'PUT',
        body: JSON.stringify({ minNoticeMinutes, maxBookingWindowDays, maxBookingsPerDay }),
      }),

    updateBookingPageMeetingSettings: (accessToken: string, pageId: string, meetingProvider: MeetingProvider) =>
      authorizedRequest<BookingPageDetailDto>(accessToken, `/api/organizer/booking-pages/${pageId}/meeting`, {
        method: 'PUT',
        body: JSON.stringify({ meetingProvider }),
      }),

    setBookingPageActive: (accessToken: string, pageId: string, isActive: boolean) =>
      authorizedRequest<BookingPageDetailDto>(accessToken, `/api/organizer/booking-pages/${pageId}/active`, {
        method: 'PATCH',
        body: JSON.stringify({ isActive }),
      }),

    deleteBookingPage: (accessToken: string, pageId: string) =>
      authorizedRequest<void>(accessToken, `/api/organizer/booking-pages/${pageId}`, { method: 'DELETE' }),

    /** Guidance shown to visitors before booking - read, never answered. */
    addBookingInstruction: (accessToken: string, pageId: string, text: string) =>
      authorizedRequest<BookingPageDetailDto>(accessToken, `/api/organizer/booking-pages/${pageId}/instructions`, {
        method: 'POST',
        body: JSON.stringify({ text }),
      }),

    removeBookingInstruction: (accessToken: string, pageId: string, instructionId: string) =>
      authorizedRequest<BookingPageDetailDto>(
        accessToken,
        `/api/organizer/booking-pages/${pageId}/instructions/${instructionId}`,
        { method: 'DELETE' },
      ),

    /** Questions visitors answer while booking - the answerable counterpart to instructions. */
    addBookingFormField: (
      accessToken: string,
      pageId: string,
      label: string,
      type: BookingFieldType,
      isRequired: boolean,
    ) =>
      authorizedRequest<BookingPageDetailDto>(accessToken, `/api/organizer/booking-pages/${pageId}/form-fields`, {
        method: 'POST',
        body: JSON.stringify({ label, type, isRequired }),
      }),

    removeBookingFormField: (accessToken: string, pageId: string, fieldId: string) =>
      authorizedRequest<BookingPageDetailDto>(
        accessToken,
        `/api/organizer/booking-pages/${pageId}/form-fields/${fieldId}`,
        { method: 'DELETE' },
      ),

    getSessions: (accessToken: string, pageId: string, status?: BookingSessionStatus) =>
      authorizedRequest<BookingSessionDto[]>(
        accessToken,
        `/api/organizer/booking-pages/${pageId}/sessions${status ? `?status=${status}` : ''}`,
      ),

    getSession: (accessToken: string, pageId: string, sessionId: string) =>
      authorizedRequest<BookingSessionDto>(accessToken, `/api/organizer/booking-pages/${pageId}/sessions/${sessionId}`),

    getTimeline: (accessToken: string, pageId: string, sessionId: string) =>
      authorizedRequest<BookingSessionEventDto[]>(
        accessToken,
        `/api/organizer/booking-pages/${pageId}/sessions/${sessionId}/timeline`,
      ),

    cancelSession: (accessToken: string, pageId: string, sessionId: string, reason?: string) =>
      authorizedRequest<BookingConfirmationDto>(
        accessToken, `/api/organizer/booking-pages/${pageId}/sessions/${sessionId}/cancel`,
        { method: 'POST', body: JSON.stringify({ reason: reason ?? null }) },
      ),

    rescheduleSession: (accessToken: string, pageId: string, sessionId: string, newDate: string, newTime: string) =>
      authorizedRequest<BookingConfirmationDto>(
        accessToken, `/api/organizer/booking-pages/${pageId}/sessions/${sessionId}/reschedule`,
        { method: 'POST', body: JSON.stringify({ newDate, newTime }) },
      ),

    resendConfirmation: (accessToken: string, pageId: string, sessionId: string) =>
      authorizedRequest<void>(
        accessToken, `/api/organizer/booking-pages/${pageId}/sessions/${sessionId}/resend-confirmation`,
        { method: 'POST' },
      ),

    getEmailHistory: (accessToken: string, pageId: string, sessionId: string) =>
      authorizedRequest<BookingSessionEventDto[]>(
        accessToken, `/api/organizer/booking-pages/${pageId}/sessions/${sessionId}/email-history`,
      ),

    /** Scheduling + delivery status of every reminder for one booking. */
    getSessionReminders: (accessToken: string, pageId: string, sessionId: string) =>
      authorizedRequest<BookingReminderDto[]>(
        accessToken, `/api/organizer/booking-pages/${pageId}/sessions/${sessionId}/reminders`,
      ),

    getNotificationSettings: (accessToken: string) =>
      authorizedRequest<NotificationSettingsDto>(accessToken, '/api/organizer/notification-settings'),

    updateNotificationSettings: (accessToken: string, settings: NotificationSettingsDto) =>
      authorizedRequest<NotificationSettingsDto>(accessToken, '/api/organizer/notification-settings', {
        method: 'PUT',
        body: JSON.stringify(settings),
      }),
  },

  /**
   * Organizer-only analytics. All four endpoints take the same filter, so the
   * dashboard passes one filter object and every panel agrees on the window.
   */
  analytics: {
    getBookings: (accessToken: string, filter: AnalyticsFilter) =>
      authorizedRequest<BookingAnalyticsDto>(accessToken, `/api/organizer/analytics/bookings${analyticsQuery(filter)}`),

    getFunnel: (accessToken: string, filter: AnalyticsFilter) =>
      authorizedRequest<ConversionFunnelDto>(accessToken, `/api/organizer/analytics/funnel${analyticsQuery(filter)}`),

    getOperations: (accessToken: string, filter: AnalyticsFilter) =>
      authorizedRequest<OperationalAnalyticsDto>(accessToken, `/api/organizer/analytics/operations${analyticsQuery(filter)}`),

    getActivity: (accessToken: string, filter: AnalyticsFilter, limit = 25) =>
      authorizedRequest<ActivityEntryDto[]>(
        accessToken,
        `/api/organizer/analytics/activity${analyticsQuery(filter, { limit: String(limit) })}`,
      ),

    /**
     * Downloads the dashboard under the current filter. Both formats take the
     * same filter object every panel is reading, so the file and the screen can
     * never describe different windows.
     */
    exportCsv: (accessToken: string, filter: AnalyticsFilter) =>
      fileRequest(
        `/api/organizer/analytics/export/csv${analyticsQuery(filter)}`,
        { headers: { Authorization: `Bearer ${accessToken}` } },
        'analytics.csv',
      ),

    exportPdf: (accessToken: string, filter: AnalyticsFilter) =>
      fileRequest(
        `/api/organizer/analytics/export/pdf${analyticsQuery(filter)}`,
        { headers: { Authorization: `Bearer ${accessToken}` } },
        'analytics.pdf',
      ),
  },

  /** Organizer-only scheduling settings - always scoped to the signed-in organizer's own resources. */
  availability: {
    getSchedule: (accessToken: string) =>
      authorizedRequest<WorkingScheduleDto | null>(accessToken, '/api/organizer/availability/schedule'),

    saveSchedule: (accessToken: string, timeZoneId: string, days: WorkingDayDto[]) =>
      authorizedRequest<WorkingScheduleDto>(accessToken, '/api/organizer/availability/schedule', {
        method: 'PUT',
        body: JSON.stringify({ timeZoneId, days }),
      }),

    getExceptions: (accessToken: string, from?: string, to?: string) => {
      const params = new URLSearchParams();
      if (from) params.set('from', from);
      if (to) params.set('to', to);
      const query = params.toString();
      return authorizedRequest<AvailabilityExceptionDto[]>(
        accessToken,
        `/api/organizer/availability/exceptions${query ? `?${query}` : ''}`,
      );
    },

    getOverrides: (accessToken: string, from?: string, to?: string) => {
      const params = new URLSearchParams();
      if (from) params.set('from', from);
      if (to) params.set('to', to);
      const query = params.toString();
      return authorizedRequest<AvailabilityOverrideDto[]>(
        accessToken,
        `/api/organizer/availability/overrides${query ? `?${query}` : ''}`,
      );
    },

    /**
     * Upsert: creates the date's override or replaces it. An empty `ranges`
     * closes the day; to fall back to the weekly schedule, delete the override.
     */
    saveOverride: (accessToken: string, date: string, ranges: TimeRangeDto[], note: string | null) =>
      authorizedRequest<AvailabilityOverrideDto>(accessToken, '/api/organizer/availability/overrides', {
        method: 'PUT',
        body: JSON.stringify({ date, ranges, note }),
      }),

    deleteOverride: (accessToken: string, overrideId: string) =>
      authorizedRequest<void>(accessToken, `/api/organizer/availability/overrides/${overrideId}`, {
        method: 'DELETE',
      }),

    /** `endDate` is optional and inclusive - omit it (or pass the same day) to block a single date. */
    createException: (
      accessToken: string,
      date: string,
      startTime: string | null,
      endTime: string | null,
      type: string,
      reason: string | null,
      endDate: string | null = null,
    ) =>
      authorizedRequest<AvailabilityExceptionDto>(accessToken, '/api/organizer/availability/exceptions', {
        method: 'POST',
        body: JSON.stringify({ date, startTime, endTime, type, reason, endDate }),
      }),

    deleteException: (accessToken: string, exceptionId: string) =>
      authorizedRequest<void>(accessToken, `/api/organizer/availability/exceptions/${exceptionId}`, {
        method: 'DELETE',
      }),

    updateSchedulingSettings: (
      accessToken: string,
      pageId: string,
      durationMinutes: number,
      bufferBeforeMinutes: number,
      bufferAfterMinutes: number,
    ) =>
      authorizedRequest<BookingPageDto>(accessToken, `/api/organizer/availability/booking-pages/${pageId}/scheduling-settings`, {
        method: 'PUT',
        body: JSON.stringify({ durationMinutes, bufferBeforeMinutes, bufferAfterMinutes }),
      }),
  },

  /** Organizer-only calendar integration (Google today; more providers can join later without a shape change here). */
  calendar: {
    getConnection: (accessToken: string) =>
      authorizedRequest<CalendarConnectionDto | null>(accessToken, '/api/calendar/connection'),

    getGoogleAuthorizeUrl: (accessToken: string, pageId: string) =>
      authorizedRequest<{ authorizationUrl: string }>(accessToken, `/api/calendar/google/connect?pageId=${pageId}`),

    disconnect: (accessToken: string) =>
      authorizedRequest<void>(accessToken, '/api/calendar/disconnect', { method: 'POST' }),

    getCalendars: (accessToken: string) =>
      authorizedRequest<ExternalCalendarDto[]>(accessToken, '/api/calendar/calendars'),

    selectCalendar: (accessToken: string, externalCalendarId: string, externalCalendarName: string) =>
      authorizedRequest<CalendarConnectionDto>(accessToken, '/api/calendar/select-calendar', {
        method: 'POST',
        body: JSON.stringify({ externalCalendarId, externalCalendarName }),
      }),

    updateSyncSettings: (accessToken: string, importBusyEvents: boolean, exportBookings: boolean) =>
      authorizedRequest<CalendarConnectionDto>(accessToken, '/api/calendar/sync-settings', {
        method: 'POST',
        body: JSON.stringify({ importBusyEvents, exportBookings }),
      }),

    updateEventSettings: (
      accessToken: string,
      eventTitleFormat: string,
      autoDeleteCancelledBookings: boolean,
      autoUpdateRescheduledBookings: boolean,
      defaultReminderMinutes: number | null,
      eventVisibility: string,
    ) =>
      authorizedRequest<CalendarConnectionDto>(accessToken, '/api/calendar/event-settings', {
        method: 'POST',
        body: JSON.stringify({
          eventTitleFormat, autoDeleteCancelledBookings, autoUpdateRescheduledBookings, defaultReminderMinutes, eventVisibility,
        }),
      }),

    /** Refreshes the token if needed, verifies calendar access, pulls fresh busy intervals, and updates the sync timestamps - never throws for a sync failure, the returned DTO's status reflects it. */
    syncNow: (accessToken: string) =>
      authorizedRequest<CalendarConnectionDto>(accessToken, '/api/calendar/sync-now', { method: 'POST' }),
  },
};
