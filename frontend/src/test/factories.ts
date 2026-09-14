import type {
  ActivityEntryDto, AvailabilityExceptionDto, AvailabilityOverrideDto, BookingAnalyticsDto, BookingFormFieldDto, BookingInstructionDto, BookingPageDetailDto,
  BookingPageDto, BookingPageSummaryDto, BookingReminderDto,
  BookingSessionDto, BookingSessionEventDto, CalendarConnectionDto, ConversionFunnelDto,
  BookingConfirmationDto, NotificationSettingsDto, OperationalAnalyticsDto, PublicBookingDto,
  WorkingDayDto, WorkingScheduleDto,
} from '../lib/types';

/**
 * Builders for the backend DTOs the UI consumes. Each takes an override object
 * so a test states only the field it is actually about - which keeps the
 * assertion visible instead of buried in twenty lines of fixture, and means a
 * new DTO field breaks one file rather than every test.
 *
 * Values mirror what the real API returns (camelCase, UTC timestamps without a
 * trailing Z where the backend omits one - see lib/dates.ts).
 */

export function bookingPageSummary(overrides: Partial<BookingPageSummaryDto> = {}): BookingPageSummaryDto {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    slug: 'demo-30-min-meeting',
    title: '30 Minute Meeting',
    description: 'A quick chat',
    isActive: true,
    durationMinutes: 30,
    bufferBeforeMinutes: 0,
    bufferAfterMinutes: 0,
    createdAt: '2026-08-01T10:00:00',
    upcomingBookingCount: 2,
    ...overrides,
  };
}

export function bookingSession(overrides: Partial<BookingSessionDto> = {}): BookingSessionDto {
  return {
    id: '22222222-2222-2222-2222-222222222222',
    bookingPageId: '11111111-1111-1111-1111-111111111111',
    status: 'Submitted',
    name: 'Jane Doe',
    email: 'jane@example.com',
    phone: '+1 555 0100',
    message: 'Looking forward to it',
    selectedDate: '2026-08-20',
    selectedTime: '09:00:00',
    createdAt: '2026-08-04T10:00:00',
    lastActivityAt: '2026-08-04T10:05:00',
    submittedAt: '2026-08-04T10:05:00',
    abandonedAt: null,
    // No meeting by default: the overwhelming majority of bookings are in
    // person, and a test about a meeting should have to say so.
    meetingProvider: null,
    meetingUrl: null,
    answers: [],
    ...overrides,
  };
}

export function sessionEvent(overrides: Partial<BookingSessionEventDto> = {}): BookingSessionEventDto {
  return {
    id: 1,
    sessionId: '22222222-2222-2222-2222-222222222222',
    bookingPageId: '11111111-1111-1111-1111-111111111111',
    eventType: 'SessionStarted',
    fieldName: null,
    oldValue: null,
    newValue: null,
    clientSequenceNumber: 0,
    timestamp: '2026-08-04T10:00:00',
    clientIp: '203.0.113.10',
    userAgent: 'test-agent/1.0',
    ...overrides,
  };
}

export function reminder(overrides: Partial<BookingReminderDto> = {}): BookingReminderDto {
  return {
    id: '33333333-3333-3333-3333-333333333333',
    minutesBeforeEvent: 1440,
    label: '24 hours',
    scheduledForUtc: '2026-08-19T07:00:00',
    meetingStartsAtUtc: '2026-08-20T07:00:00',
    status: 'Scheduled',
    channel: 'Email',
    queuedAtUtc: null,
    sentAtUtc: null,
    attemptCount: 0,
    failureReason: null,
    resolutionReason: null,
    ...overrides,
  };
}

export function notificationSettings(overrides: Partial<NotificationSettingsDto> = {}): NotificationSettingsDto {
  return {
    notifyGuestOnBooking: true,
    notifyOrganizerOnBooking: true,
    remindersEnabled: true,
    reminderMinutesBeforeEvent: [1440],
    notifyOrganizerOnReminderSent: false,
    ...overrides,
  };
}

export function calendarConnection(overrides: Partial<CalendarConnectionDto> = {}): CalendarConnectionDto {
  return {
    id: '44444444-4444-4444-4444-444444444444',
    provider: 'Google',
    externalAccountEmail: 'organizer@gmail.com',
    externalCalendarId: 'primary',
    externalCalendarName: 'Work',
    status: 'Connected',
    healthStatus: 'Connected',
    lastSyncError: null,
    lastSuccessfulSyncAtUtc: '2026-08-04T18:15:00',
    lastFailedSyncAtUtc: null,
    importBusyEvents: true,
    exportBookings: true,
    autoDeleteCancelledBookings: true,
    autoUpdateRescheduledBookings: true,
    defaultReminderMinutes: null,
    eventVisibility: 'default',
    eventTitleFormat: '{Service Name} with {Guest Name}',
    syncedBookingCount: 7,
    ...overrides,
  };
}

export function bookingAnalytics(overrides: Partial<BookingAnalyticsDto> = {}): BookingAnalyticsDto {
  return {
    summary: {
      totalSessions: 20,
      confirmed: 8,
      cancelled: 3,
      abandoned: 6,
      inProgress: 3,
      upcoming: 5,
      completed: 3,
      rescheduled: 2,
      totalBookingPages: 2,
      activeBookingPages: 1,
      completionRate: 0.4,
    },
    trend: [
      { date: '2026-08-01', sessions: 4, bookings: 2 },
      { date: '2026-08-02', sessions: 0, bookings: 0 },
      { date: '2026-08-03', sessions: 6, bookings: 3 },
    ],
    statusDistribution: [
      { status: 'Upcoming', count: 5, share: 0.25 },
      { status: 'Completed', count: 3, share: 0.15 },
      { status: 'Cancelled', count: 3, share: 0.15 },
      { status: 'Abandoned', count: 6, share: 0.3 },
      { status: 'In progress', count: 3, share: 0.15 },
    ],
    bookingsByWeekday: [
      { dayOfWeek: 0, label: 'Sunday', count: 0 },
      { dayOfWeek: 1, label: 'Monday', count: 4 },
      { dayOfWeek: 2, label: 'Tuesday', count: 2 },
      { dayOfWeek: 3, label: 'Wednesday', count: 0 },
      { dayOfWeek: 4, label: 'Thursday', count: 1 },
      { dayOfWeek: 5, label: 'Friday', count: 1 },
      { dayOfWeek: 6, label: 'Saturday', count: 0 },
    ],
    bookingsByHour: [
      { hour: 9, count: 3 },
      { hour: 10, count: 4 },
      { hour: 11, count: 1 },
    ],
    completionTime: {
      averageSeconds: 125,
      medianSeconds: 90,
      fastestSeconds: 30,
      slowestSeconds: 400,
      sampleSize: 8,
    },
    pagePerformance: [
      {
        bookingPageId: '11111111-1111-1111-1111-111111111111',
        title: '30 Minute Meeting',
        slug: 'demo-30-min-meeting',
        isActive: true,
        views: 14,
        bookings: 6,
        cancelled: 2,
        upcoming: 4,
        conversionRate: 0.4286,
        cancellationRate: 0.25,
      },
    ],
    timeZoneId: 'Europe/Skopje',
    ...overrides,
  };
}

export function conversionFunnel(overrides: Partial<ConversionFunnelDto> = {}): ConversionFunnelDto {
  return {
    steps: [
      { step: 'Page viewed', count: 20, shareOfEntry: 1, stepConversion: 1 },
      { step: 'Date selected', count: 14, shareOfEntry: 0.7, stepConversion: 0.7 },
      { step: 'Time selected', count: 12, shareOfEntry: 0.6, stepConversion: 0.857 },
      { step: 'Details entered', count: 10, shareOfEntry: 0.5, stepConversion: 0.833 },
      { step: 'Booking confirmed', count: 8, shareOfEntry: 0.4, stepConversion: 0.8 },
    ],
    conversionRate: 0.4,
    abandonment: {
      totalAbandoned: 6,
      abandonmentRate: 0.3,
      averageStepReached: 2.5,
      mostCommonStep: 'Date selected',
      byStep: [
        { step: 'Date selected', count: 4, share: 0.667 },
        { step: 'Page viewed', count: 2, share: 0.333 },
      ],
    },
    ...overrides,
  };
}

export function operationalAnalytics(overrides: Partial<OperationalAnalyticsDto> = {}): OperationalAnalyticsDto {
  return {
    email: {
      total: 30, pending: 1, sent: 28, failed: 1, retries: 2, deliveryRate: 0.9655,
      byType: [
        { notificationType: 'BookingConfirmation', total: 12, sent: 12, failed: 0 },
        { notificationType: 'Reminder', total: 6, sent: 5, failed: 1 },
      ],
    },
    reminders: {
      total: 10, scheduled: 4, sent: 4, failed: 0, skipped: 1, cancelled: 1, averageLeadTimeMinutes: 970,
    },
    calendar: {
      connected: true,
      accountEmail: 'organizer@gmail.com',
      calendarName: 'Work',
      status: 'Connected',
      healthStatus: 'Connected',
      syncedBookings: 6,
      confirmedBookings: 8,
      syncCoverage: 0.75,
      lastSuccessfulSyncAtUtc: '2026-08-04T18:15:00',
      lastFailedSyncAtUtc: null,
      lastSyncError: null,
    },
    ...overrides,
  };
}

export function activityEntry(overrides: Partial<ActivityEntryDto> = {}): ActivityEntryDto {
  return {
    kind: 'BookingConfirmed',
    description: 'Booking confirmed',
    occurredAtUtc: '2026-08-04T10:05:00',
    bookingPageId: '11111111-1111-1111-1111-111111111111',
    sessionId: '22222222-2222-2222-2222-222222222222',
    ...overrides,
  };
}

export function bookingInstruction(overrides: Partial<BookingInstructionDto> = {}): BookingInstructionDto {
  return {
    id: '66666666-6666-6666-6666-666666666666',
    text: 'Please have your account number ready',
    displayOrder: 0,
    ...overrides,
  };
}

/** Organizer-owned view, backing the settings screens. */
export function bookingPageDetail(overrides: Partial<BookingPageDetailDto> = {}): BookingPageDetailDto {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    slug: 'demo-30-min-meeting',
    title: '30 Minute Meeting',
    description: 'A quick chat',
    isActive: true,
    durationMinutes: 30,
    bufferBeforeMinutes: 0,
    bufferAfterMinutes: 0,
    minNoticeMinutes: null,
    maxBookingWindowDays: null,
    maxBookingsPerDay: null,
    createdAt: '2026-08-01T10:00:00',
    meetingProvider: 'None',
    instructions: [],
    formFields: [],
    ...overrides,
  };
}

/** Public view, backing the booking wizard. */
export function bookingPage(overrides: Partial<BookingPageDto> = {}): BookingPageDto {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    slug: 'demo-30-min-meeting',
    title: '30 Minute Meeting',
    description: 'A quick chat',
    organizerName: 'Demo Organizer',
    durationMinutes: 30,
    bufferBeforeMinutes: 0,
    bufferAfterMinutes: 0,
    meetingProvider: 'None',
    // The organizer's zone, as the seeded demo organizer has it. A test about
    // a visitor in a different zone overrides one side or the other.
    timeZoneId: 'Europe/Skopje',
    instructions: [],
    formFields: [],
    ...overrides,
  };
}

/** One organizer-defined question a visitor answers. Short text and optional unless a test says otherwise. */
/** What Submit/Cancel/Reschedule return - the only DTO carrying the PublicToken. */
export function bookingConfirmation(overrides: Partial<BookingConfirmationDto> = {}): BookingConfirmationDto {
  return {
    id: '22222222-2222-2222-2222-222222222222',
    bookingPageId: '11111111-1111-1111-1111-111111111111',
    status: 'Submitted',
    name: 'Jane Doe',
    email: 'jane@example.com',
    phone: '+1 555 0100',
    message: null,
    selectedDate: '2026-08-20',
    selectedTime: '09:00:00',
    createdAt: '2026-08-04T10:00:00',
    lastActivityAt: '2026-08-04T10:05:00',
    submittedAt: '2026-08-04T10:05:00',
    abandonedAt: null,
    cancelledAt: null,
    rescheduledAt: null,
    bookingReference: 'BK-12345',
    publicToken: 'public-token-abc',
    meetingProvider: null,
    meetingUrl: null,
    answers: [],
    // The ordinary case: the organizer has guest notifications on, so a
    // confirmation was written to the queue. A test about the other state has
    // to ask for it.
    guestConfirmationQueued: true,
    ...overrides,
  };
}

/** Public view, backing the guest's "manage my booking" page. */
export function publicBooking(overrides: Partial<PublicBookingDto> = {}): PublicBookingDto {
  return {
    bookingReference: 'BK-12345',
    bookingPageSlug: 'demo-30-min-meeting',
    organizerName: 'Demo Organizer',
    organizerEmail: 'organizer@example.com',
    serviceTitle: '30 Minute Meeting',
    durationMinutes: 30,
    selectedDate: '2026-08-20',
    selectedTime: '09:00:00',
    timeZoneId: 'Europe/Skopje',
    // 09:00 in Europe/Skopje on that date is 07:00Z - the pair the backend
    // resolves, not something a test should derive from the wall clock above.
    startUtc: '2026-08-20T07:00:00Z',
    endUtc: '2026-08-20T07:30:00Z',
    status: 'Submitted',
    name: 'Jane Doe',
    email: 'jane@example.com',
    canCancel: true,
    canReschedule: true,
    // No meeting by default - a test about one has to ask for it.
    meetingProvider: null,
    meetingUrl: null,
    // No reminder promised by default, for the same reason: the screen may only
    // mention one when the backend says a row exists.
    reminderLeadMinutes: [],
    ...overrides,
  };
}

/** One date's specific opening hours. Open 09:00-17:00 by default; a test about a closed day has to say so. */
export function availabilityOverride(overrides: Partial<AvailabilityOverrideDto> = {}): AvailabilityOverrideDto {
  return {
    id: '77777777-7777-7777-7777-777777777777',
    date: '2026-08-15',
    isClosed: false,
    ranges: [{ start: '09:00:00', end: '17:00:00' }],
    note: null,
    ...overrides,
  };
}

/**
 * One blocked day. A single whole day by default - `endDate`/`totalDays` are a
 * range's own concern and a test about one states both, the way the backend does.
 */
export function availabilityException(
  overrides: Partial<AvailabilityExceptionDto> = {},
): AvailabilityExceptionDto {
  return {
    id: '88888888-8888-8888-8888-888888888888',
    date: '2026-08-25',
    endDate: '2026-08-25',
    totalDays: 1,
    startTime: null,
    endTime: null,
    type: 'Vacation',
    reason: null,
    ...overrides,
  };
}

/**
 * A saved weekly schedule. Defaults to the shape a new organizer is seeded with
 * - Monday-Friday 09:00-17:00, weekend closed - so a test that only needs "this
 * organizer can be booked" says nothing at all.
 */
export function workingSchedule(overrides: Partial<WorkingScheduleDto> = {}): WorkingScheduleDto {
  const days: WorkingDayDto[] = [0, 1, 2, 3, 4, 5, 6].map((d) => ({
    dayOfWeek: d as WorkingDayDto['dayOfWeek'],
    isEnabled: d >= 1 && d <= 5,
    intervals: d >= 1 && d <= 5 ? [{ start: '09:00:00', end: '17:00:00' }] : [],
  }));

  return {
    id: '88888888-8888-8888-8888-888888888888',
    organizerId: '99999999-9999-9999-9999-999999999999',
    timeZoneId: 'Europe/Skopje',
    days,
    ...overrides,
  };
}

export function bookingFormField(overrides: Partial<BookingFormFieldDto> = {}): BookingFormFieldDto {
  return {
    id: '77777777-7777-7777-7777-777777777777',
    label: 'Company',
    type: 'ShortText',
    isRequired: false,
    displayOrder: 0,
    ...overrides,
  };
}
