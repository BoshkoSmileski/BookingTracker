export const API_BASE_URL: string =
  import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5216';

export const HUB_URL = `${API_BASE_URL}/hubs/organizer-dashboard`;

/** Frontend keeps this in sync with BookingTracker.Domain.Common.BookingFieldNames. */
export const BookingFieldNames = {
  Name: 'Name',
  Email: 'Email',
  Phone: 'Phone',
  Message: 'Message',
  Date: 'Date',
  Time: 'Time',
} as const;

/** Frontend keeps this in sync with BookingTracker.Domain.Enums.BookingEventType. */
export const BookingEventType = {
  SessionStarted: 'SessionStarted',
  FieldChanged: 'FieldChanged',
  DateSelected: 'DateSelected',
  TimeSelected: 'TimeSelected',
  UserInactive: 'UserInactive',
  UserActive: 'UserActive',
  BrowserClosed: 'BrowserClosed',
  BookingSubmitted: 'BookingSubmitted',
  BookingAbandoned: 'BookingAbandoned',
  MeetingLinkAssigned: 'MeetingLinkAssigned',
} as const;

export const IDLE_THRESHOLD_MS = 15_000;
export const FLUSH_DEBOUNCE_MS = 400;

/**
 * Mirrors BookingFieldLimits.CustomAnswerMaxLength. Applied as `maxLength` on
 * the inputs so a visitor is stopped at the limit rather than having a batch
 * rejected mid-typing - the server enforces the same number regardless.
 */
export const CUSTOM_ANSWER_MAX_LENGTH = 2000;

/** Mirrors BookingFieldLimits.CustomFieldLabelMaxLength. */
export const CUSTOM_FIELD_LABEL_MAX_LENGTH = 150;

/** Mirrors BookingPage.MaxFormFields. */
export const MAX_FORM_FIELDS = 10;
