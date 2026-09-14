export type BookingSessionStatus = 'Active' | 'Submitted' | 'Abandoned' | 'Cancelled';

/**
 * Mirrors BookingTracker.Domain.Enums.MeetingProviderType. `None` is a booking
 * page setting only - a *session* carries null when it has no meeting, never
 * the string 'None', because the session's value is only ever written when a
 * meeting is actually created for it.
 */
export type MeetingProvider = 'None' | 'GoogleMeet';

export interface BookingPageDto {
  id: string;
  slug: string;
  title: string;
  description: string | null;
  organizerName: string;
  durationMinutes: number;
  bufferBeforeMinutes: number;
  bufferAfterMinutes: number;
  /** How bookings on this page meet. Known before booking; the join link itself only exists afterwards. */
  meetingProvider: MeetingProvider;
  /**
   * The organizer's IANA time zone - the clock every time this page shows is
   * on (`AvailableSlotDto.localStartTime`, the selected time, the confirmation).
   * A label for values already sent, never something the client converts with:
   * see lib/timezone.ts.
   */
  timeZoneId: string;
  /** What the organizer wants visitors to read before booking. Empty when none are set. */
  instructions: BookingInstructionDto[];
  /** Questions the visitor answers on the details step. Empty when none are set. */
  formFields: BookingFormFieldDto[];
}

export type CalendarProvider = 'Google';
export type CalendarSyncStatus = 'Connected' | 'ReauthorizationRequired' | 'Error' | 'CalendarNotFound';
export type CalendarEventVisibility = 'default' | 'public' | 'private';

export interface CalendarConnectionDto {
  id: string;
  provider: CalendarProvider;
  externalAccountEmail: string;
  externalCalendarId: string;
  externalCalendarName: string;
  status: CalendarSyncStatus;
  /** Organizer-facing label for `status`, e.g. "Needs Reauthentication". */
  healthStatus: string;
  lastSyncError: string | null;
  lastSuccessfulSyncAtUtc: string | null;
  lastFailedSyncAtUtc: string | null;
  importBusyEvents: boolean;
  exportBookings: boolean;
  autoDeleteCancelledBookings: boolean;
  autoUpdateRescheduledBookings: boolean;
  defaultReminderMinutes: number | null;
  eventVisibility: CalendarEventVisibility;
  eventTitleFormat: string;
  syncedBookingCount: number;
}

export interface ExternalCalendarDto {
  id: string;
  name: string;
  isPrimary: boolean;
}

/** Mirrors Application.Notifications.Dtos.NotificationSettingsDto - defaults apply until the organizer saves their own. */
export interface NotificationSettingsDto {
  notifyGuestOnBooking: boolean;
  notifyOrganizerOnBooking: boolean;
  remindersEnabled: boolean;
  /** Minutes before the appointment - e.g. [1440] for a single 24-hour reminder. */
  reminderMinutesBeforeEvent: number[];
  /** Opt-in copy to the organizer each time a guest reminder goes out. */
  notifyOrganizerOnReminderSent: boolean;
}

/**
 * Flattened outcome of one reminder - the backend merges scheduling state
 * (BookingReminder) with delivery state (EmailNotification) into this single
 * status, so the UI never has to reason about the two separately.
 */
export type BookingReminderStatus = 'Scheduled' | 'Queued' | 'Sent' | 'Failed' | 'Cancelled' | 'Skipped';

// ---------------------------------------------------------------------------
// Analytics - mirrors Application.Analytics.Dtos.*
// ---------------------------------------------------------------------------

/** Shared filter for every analytics endpoint. Organizer comes from the token, never the client. */
export interface AnalyticsFilter {
  from?: string | null;
  to?: string | null;
  bookingPageId?: string | null;
  status?: BookingSessionStatus | null;
}

/**
 * A file the API returned rather than JSON, with the name the server chose.
 * Not a backend DTO - the API sends bytes plus a Content-Disposition header, and
 * this is the shape lib/api.ts resolves those two into.
 */
export interface DownloadedFile {
  blob: Blob;
  fileName: string;
}

export type AnalyticsExportFormat = 'csv' | 'pdf';

export interface BookingSummaryDto {
  totalSessions: number;
  confirmed: number;
  cancelled: number;
  abandoned: number;
  inProgress: number;
  upcoming: number;
  completed: number;
  /** Bookings moved at least once. Overlaps the other categories, so it is a card and never a pie slice. */
  rescheduled: number;
  totalBookingPages: number;
  activeBookingPages: number;
  /** 0-1. */
  completionRate: number;
}

export interface TrendPointDto {
  date: string;
  sessions: number;
  bookings: number;
}

export interface StatusSliceDto {
  status: string;
  count: number;
  share: number;
}

export interface WeekdayCountDto {
  dayOfWeek: number;
  label: string;
  count: number;
}

export interface HourCountDto {
  hour: number;
  count: number;
}

export interface CompletionTimeDto {
  averageSeconds: number | null;
  medianSeconds: number | null;
  fastestSeconds: number | null;
  slowestSeconds: number | null;
  sampleSize: number;
}

export interface BookingPagePerformanceDto {
  bookingPageId: string;
  title: string;
  slug: string;
  isActive: boolean;
  views: number;
  bookings: number;
  cancelled: number;
  upcoming: number;
  conversionRate: number;
  cancellationRate: number;
}

export interface BookingAnalyticsDto {
  summary: BookingSummaryDto;
  trend: TrendPointDto[];
  statusDistribution: StatusSliceDto[];
  bookingsByWeekday: WeekdayCountDto[];
  bookingsByHour: HourCountDto[];
  completionTime: CompletionTimeDto;
  pagePerformance: BookingPagePerformanceDto[];
  timeZoneId: string;
}

export interface FunnelStepDto {
  step: string;
  count: number;
  shareOfEntry: number;
  stepConversion: number;
}

export interface AbandonmentStepDto {
  step: string;
  count: number;
  share: number;
}

export interface AbandonmentDto {
  totalAbandoned: number;
  abandonmentRate: number;
  averageStepReached: number | null;
  mostCommonStep: string | null;
  byStep: AbandonmentStepDto[];
}

export interface ConversionFunnelDto {
  steps: FunnelStepDto[];
  conversionRate: number;
  abandonment: AbandonmentDto;
}

export interface EmailTypeCountDto {
  notificationType: string;
  total: number;
  sent: number;
  failed: number;
}

export interface EmailAnalyticsDto {
  total: number;
  pending: number;
  sent: number;
  failed: number;
  retries: number;
  deliveryRate: number;
  byType: EmailTypeCountDto[];
}

export interface ReminderAnalyticsDto {
  total: number;
  scheduled: number;
  sent: number;
  failed: number;
  skipped: number;
  cancelled: number;
  averageLeadTimeMinutes: number | null;
}

export interface CalendarAnalyticsDto {
  connected: boolean;
  accountEmail: string | null;
  calendarName: string | null;
  status: string | null;
  healthStatus: string | null;
  syncedBookings: number;
  confirmedBookings: number;
  /** Synced / confirmed, 0-1. Not a per-attempt success rate - see the backend DTO. */
  syncCoverage: number | null;
  lastSuccessfulSyncAtUtc: string | null;
  lastFailedSyncAtUtc: string | null;
  lastSyncError: string | null;
}

export interface OperationalAnalyticsDto {
  email: EmailAnalyticsDto;
  reminders: ReminderAnalyticsDto;
  calendar: CalendarAnalyticsDto;
}

export type ActivityKind =
  | 'BookingConfirmed'
  | 'BookingCancelled'
  | 'BookingRescheduled'
  | 'ReminderSent'
  | 'EmailSent'
  | 'EmailFailed'
  | 'BookingPageCreated';

export interface ActivityEntryDto {
  kind: ActivityKind;
  description: string;
  occurredAtUtc: string;
  bookingPageId: string | null;
  sessionId: string | null;
}

/** Mirrors Application.Notifications.Dtos.BookingReminderDto. */
export interface BookingReminderDto {
  id: string;
  minutesBeforeEvent: number;
  label: string;
  scheduledForUtc: string;
  meetingStartsAtUtc: string;
  status: BookingReminderStatus;
  channel: string;
  queuedAtUtc: string | null;
  sentAtUtc: string | null;
  attemptCount: number;
  failureReason: string | null;
  resolutionReason: string | null;
}

/**
 * One line of guidance shown to visitors before they book, e.g. "Please have
 * your account number ready". Deliberately not a question - there is no answer
 * field and visitors never type anything in response.
 *
 * The backend entity behind it is still called BookingQuestion; the rename stops
 * at the Application layer so no migration was needed.
 */
export interface BookingInstructionDto {
  id: string;
  text: string;
  displayOrder: number;
}

/** Which input control a custom field renders as - it governs nothing else, including the length limit. */
export type BookingFieldType = 'ShortText' | 'LongText';

/**
 * One organizer-defined question a visitor answers while booking.
 *
 * The answerable counterpart to BookingInstructionDto above. The backend entity
 * is BookingFormField; nothing anywhere calls either of these a "question" in
 * code, because Domain's BookingQuestion already means an instruction.
 */
export interface BookingFormFieldDto {
  id: string;
  label: string;
  type: BookingFieldType;
  isRequired: boolean;
  displayOrder: number;
}

/**
 * A visitor's answer to one custom field, keyed by field id rather than label -
 * both screens that show answers already hold the page's `formFields`, so the
 * label is joined there. See `answersByFieldId` in lib/bookingForm.ts.
 */
export interface BookingSessionAnswerDto {
  fieldId: string;
  value: string;
}

/** Backs the organizer's "all my booking pages" workspace list - richer than BookingPageDto, never exposed publicly. */
export interface BookingPageSummaryDto {
  id: string;
  slug: string;
  title: string;
  description: string | null;
  isActive: boolean;
  durationMinutes: number;
  bufferBeforeMinutes: number;
  bufferAfterMinutes: number;
  createdAt: string;
  upcomingBookingCount: number;
}

/** Full organizer-owned view of a booking page - backs the create/edit settings pages. */
export interface BookingPageDetailDto {
  id: string;
  slug: string;
  title: string;
  description: string | null;
  isActive: boolean;
  durationMinutes: number;
  bufferBeforeMinutes: number;
  bufferAfterMinutes: number;
  minNoticeMinutes: number | null;
  maxBookingWindowDays: number | null;
  maxBookingsPerDay: number | null;
  createdAt: string;
  meetingProvider: MeetingProvider;
  instructions: BookingInstructionDto[];
  formFields: BookingFormFieldDto[];
}

export interface CreateBookingPageRequest {
  title: string;
  description: string | null;
  durationMinutes: number;
  bufferBeforeMinutes: number;
  bufferAfterMinutes: number;
  minNoticeMinutes: number | null;
  maxBookingWindowDays: number | null;
  maxBookingsPerDay: number | null;
  /**
   * The organizer's IANA zone, as the browser resolved it. Optional, and a hint
   * rather than a setting: the server uses it only to decide which clock a
   * FIRST working schedule is created on, and ignores it entirely once the
   * organizer has one.
   */
  timeZoneId?: string;
}

export interface BookingSessionDto {
  id: string;
  bookingPageId: string;
  status: BookingSessionStatus;
  name: string | null;
  email: string | null;
  phone: string | null;
  message: string | null;
  selectedDate: string | null;
  selectedTime: string | null;
  createdAt: string;
  lastActivityAt: string;
  submittedAt: string | null;
  abandonedAt: string | null;
  /** The booking's online meeting, or null when it has none. */
  meetingProvider: MeetingProvider | null;
  meetingUrl: string | null;
  /** Answers to the page's custom fields. Empty when the page has none. */
  answers: BookingSessionAnswerDto[];
}

export interface BookingSessionEventDto {
  id: number;
  sessionId: string;
  bookingPageId: string;
  eventType: string;
  fieldName: string | null;
  oldValue: string | null;
  newValue: string | null;
  clientSequenceNumber: number;
  timestamp: string;
  clientIp: string | null;
  userAgent: string | null;
}

/** Outbound shape - mirrors Application.BookingSessions.Dtos.ClientBookingEventDto. */
export interface ClientBookingEventDto {
  eventType: string;
  fieldName: string | null;
  newValue: string | null;
  clientSequenceNumber: number;
}

export interface ApiProblem {
  title: string;
  errors?: Record<string, string[]>;
}

/** Mirrors Application.Auth.Dtos.AuthResultDto. */
export interface AuthResultDto {
  organizerId: string;
  name: string;
  email: string;
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
}

/** .NET's System.DayOfWeek: Sunday = 0 ... Saturday = 6. Serializes as a plain number. */
export type DayOfWeekNumber = 0 | 1 | 2 | 3 | 4 | 5 | 6;

export interface TimeRangeDto {
  start: string; // "HH:mm:ss"
  end: string;
}

export interface WorkingDayDto {
  dayOfWeek: DayOfWeekNumber;
  isEnabled: boolean;
  intervals: TimeRangeDto[];
}

export interface WorkingScheduleDto {
  id: string;
  organizerId: string;
  timeZoneId: string;
  days: WorkingDayDto[];
}

export type AvailabilityExceptionType = 'Holiday' | 'Vacation' | 'Meeting' | 'SickLeave' | 'Training' | 'Other';

/**
 * One date's specific opening hours, replacing the weekly schedule on that date.
 * Mirrors Application.Availability.Dtos.AvailabilityOverrideDto.
 */
export interface AvailabilityOverrideDto {
  id: string;
  /** The date these hours apply to, "yyyy-MM-dd". A wall-clock date, not a UTC instant. */
  date: string;
  /** True when the day is closed. Sent by the backend so no screen re-derives what an empty `ranges` means. */
  isClosed: boolean;
  /** Open hours, ordered by start. Empty when closed. */
  ranges: TimeRangeDto[];
  note: string | null;
}

export interface AvailabilityExceptionDto {
  id: string;
  /** Inclusive first day, "yyyy-MM-dd". */
  date: string;
  /** Inclusive last day. Equal to `date` for a single-day exception. */
  endDate: string;
  /** Days covered, inclusive of both ends. Sent by the backend so the UI never re-derives an off-by-one. */
  totalDays: number;
  /** When set, only this window is blocked - on every day of the range. */
  startTime: string | null;
  endTime: string | null;
  type: AvailabilityExceptionType;
  reason: string | null;
}

export interface AvailableSlotDto {
  localDate: string;
  localStartTime: string;
  localEndTime: string;
  startUtc: string;
  endUtc: string;
}

/** Mirrors Application.Bookings.Dtos.BookingConfirmationDto - only ever returned by Submit/Cancel/Reschedule. */
export interface BookingConfirmationDto {
  id: string;
  bookingPageId: string;
  status: BookingSessionStatus;
  name: string | null;
  email: string | null;
  phone: string | null;
  message: string | null;
  selectedDate: string | null;
  selectedTime: string | null;
  createdAt: string;
  lastActivityAt: string;
  submittedAt: string | null;
  abandonedAt: string | null;
  cancelledAt: string | null;
  rescheduledAt: string | null;
  bookingReference: string | null;
  publicToken: string | null;
  /** The booking's online meeting, or null. Present on the submit response, so the success step can offer it immediately. */
  meetingProvider: MeetingProvider | null;
  meetingUrl: string | null;
  answers: BookingSessionAnswerDto[];
  /**
   * Whether a guest-facing confirmation of this action was queued - the booking
   * confirmation on submit, the cancellation confirmation on cancel, the
   * reschedule confirmation on reschedule.
   *
   * **Queued, not sent.** The backend writes an `EmailNotifications` row and
   * `EmailQueueProcessor` transmits it seconds later, out of the request, so
   * "queued" is the strongest claim the response can make. False is ordinary,
   * not an error: the organizer may have guest notifications switched off, or
   * queueing (which is best-effort and never fails a booking) may have failed.
   * Screens key their "on its way" line off this and say nothing about email
   * when it is false, rather than promising one nobody wrote.
   */
  guestConfirmationQueued: boolean;
}

/** Mirrors Application.Bookings.Dtos.PublicBookingDto - what the public "manage my booking" page sees. */
export interface PublicBookingDto {
  bookingReference: string;
  bookingPageSlug: string;
  organizerName: string;
  /** The organizer's own address, so a guest has a way to reach them - particularly once the booking is past or cancelled and there is nothing left to change. */
  organizerEmail: string;
  serviceTitle: string;
  durationMinutes: number;
  selectedDate: string | null;
  selectedTime: string | null;
  /** The organizer's IANA time zone - whose clock `selectedTime` is on. */
  timeZoneId: string;
  /**
   * The booking's real instant, and the end of it - the same pair
   * `AvailableSlotDto` carries, resolved by the backend against the organizer's
   * own `TimeZoneInfo`.
   *
   * Use these, and only these, for anything that needs the actual moment (the
   * calendar file). Never rebuild one from `selectedDate`/`selectedTime`: those
   * are organizer-local wall clock, and parsing them here reads them in the
   * *visitor's* zone - the bug the wizard's .ics download used to have. They
   * are already `Kind=Utc` on the wire, so they carry a `Z` and need no
   * `lib/dates.ts` round trip, exactly like `AvailableSlotDto.startUtc`.
   *
   * Null only for a booking with no date or time, which a submitted one is not.
   */
  startUtc: string | null;
  endUtc: string | null;
  status: BookingSessionStatus;
  name: string | null;
  email: string | null;
  canCancel: boolean;
  canReschedule: boolean;
  /**
   * The booking's online meeting, or null. The backend supplies these only
   * while the booking is still live (the same condition as canCancel /
   * canReschedule), so a guest is never offered a join button for a meeting
   * that is cancelled or already over.
   */
  meetingProvider: MeetingProvider | null;
  meetingUrl: string | null;
  /**
   * Lead times (minutes before the meeting) of the reminders still scheduled
   * for this booking, ascending. Read from the actual `BookingReminders` rows,
   * so an empty list genuinely means "no reminder is coming" — reminders off,
   * cancelled with the booking, or already sent. Never say a reminder is coming
   * on any other basis.
   */
  reminderLeadMinutes: number[];
}
