import { describe, expect, it } from 'vitest';
import { ApiError, NetworkError, errorMessage, validationErrors } from '../api';
import { hasBookableAvailability } from '../availability';
import { DAY_DISPLAY_ORDER, DAY_NAMES } from '../dayOfWeek';
import type { WorkingScheduleDto } from '../types';

/**
 * REGRESSION AREA: errorMessage. The old pattern
 * (`e instanceof ApiError ? e.problem.title : 'Failed to X.'`) reported an
 * unreachable server using the caller's own domain wording ("Failed to
 * register."), which points debugging at the wrong layer. The split between
 * ApiError (server answered, and rejected) and NetworkError (never reached the
 * server) is what these tests pin.
 */
describe('errorMessage', () => {
  it('uses the server’s own message when the request was rejected', () => {
    const error = new ApiError(400, { title: 'An organizer with this email already exists.' });

    expect(errorMessage(error, 'Failed to register.')).toBe('An organizer with this email already exists.');
  });

  it('reports connectivity - not the caller’s domain wording - when the server was unreachable', () => {
    const message = errorMessage(new NetworkError(), 'Failed to register.');

    expect(message).not.toBe('Failed to register.');
    expect(message).toMatch(/could not reach the server/i);
  });

  it('lets an organizer-facing screen override the connectivity wording', () => {
    const message = errorMessage(new NetworkError(), 'Failed to sign in.', 'Could not reach the server. Check that the API is running.');

    expect(message).toBe('Could not reach the server. Check that the API is running.');
  });

  it('falls back to the caller’s message only for a genuinely unexpected error', () => {
    expect(errorMessage(new TypeError('x is not a function'), 'Failed to cancel this booking.'))
      .toBe('Failed to cancel this booking.');
    expect(errorMessage('a thrown string', 'Failed to cancel this booking.'))
      .toBe('Failed to cancel this booking.');
  });

  it('keeps ApiError’s status available for callers that branch on it', () => {
    // DashboardHomePage branches on 409, callProtected on 401 - the message helper
    // must not be the only thing an ApiError is good for.
    const conflict = new ApiError(409, { title: 'Page has bookings.' });

    expect(conflict.status).toBe(409);
    expect(conflict).toBeInstanceOf(ApiError);
    expect(new NetworkError()).not.toBeInstanceOf(ApiError);
  });
});

/**
 * REGRESSION AREA: server validation messages reaching a human.
 *
 * ExceptionHandlingMiddleware answers every FluentValidation failure with the
 * title "Validation failed" and puts the real explanation in `errors`. Nothing
 * read `errors`, so ~33 hand-written backend messages - the IANA time zone one,
 * the overlapping-interval one, "{Label} is required." for a booking question -
 * all rendered as those two useless words.
 *
 * Key shapes here are the ones the running API actually produces, confirmed
 * against it rather than assumed: PascalCase property names, indexed paths,
 * `""` for a rule written against the request root, and `$` from ASP.NET's own
 * model-binding ProblemDetails.
 */
describe('errorMessage — validation rejections', () => {
  it('shows the server’s field messages instead of the "Validation failed" title', () => {
    const error = new ApiError(400, {
      title: 'Validation failed',
      errors: { TimeZoneId: ["'Europe/Skopj' is not a recognized IANA time zone id."] },
    });

    const message = errorMessage(error, 'Failed to save schedule.');

    expect(message).toBe("'Europe/Skopj' is not a recognized IANA time zone id.");
    expect(message).not.toMatch(/validation failed/i);
  });

  it('joins several messages rather than showing only the first', () => {
    const error = new ApiError(400, {
      title: 'Validation failed',
      errors: {
        TimeZoneId: ['Time zone is not recognized.'],
        'Days[0].Intervals': ['Intervals within a single day must not overlap.'],
      },
    });

    const message = errorMessage(error, 'Failed to save schedule.');

    expect(message).toContain('Time zone is not recognized.');
    expect(message).toContain('Intervals within a single day must not overlap.');
  });

  it('says one broken rule once, however many items broke it', () => {
    // A batch of tracked events reports the same rule per event, so without
    // this a guest would be shown the same sentence two hundred times.
    const error = new ApiError(400, {
      title: 'Validation failed',
      errors: {
        'Events[0].NewValue': ['Name cannot exceed 200 characters.'],
        'Events[1].NewValue': ['Name cannot exceed 200 characters.'],
        'Events[2].NewValue': ['Name cannot exceed 200 characters.'],
      },
    });

    expect(errorMessage(error, 'fallback')).toBe('Name cannot exceed 200 characters.');
  });

  it('surfaces a message keyed against the request root, which names no field', () => {
    // RuleFor(x => x) produces an empty key - GetAvailableSlotsQueryValidator does.
    const error = new ApiError(400, {
      title: 'Validation failed',
      errors: { '': ['ToDate must not be before FromDate.'] },
    });

    expect(errorMessage(error, 'fallback')).toBe('ToDate must not be before FromDate.');
  });

  it('surfaces ASP.NET’s own model-binding errors, which use the same shape', () => {
    const error = new ApiError(400, {
      title: 'One or more validation errors occurred.',
      errors: { events: ['The events field is required.'] },
    });

    expect(errorMessage(error, 'fallback')).toBe('The events field is required.');
  });

  it('still uses the title when the rejection carries no field errors', () => {
    // A 409, a 404, a domain exception - none of these have an `errors` map, and
    // their titles are written for a user.
    const error = new ApiError(409, { title: 'That time has just been taken. Please choose another time.' });

    expect(errorMessage(error, 'fallback')).toBe('That time has just been taken. Please choose another time.');
  });

  it('falls back to the title when `errors` is present but says nothing', () => {
    const empty = new ApiError(400, { title: 'Validation failed', errors: {} });
    const blank = new ApiError(400, { title: 'Validation failed', errors: { Label: [] } });

    expect(errorMessage(empty, 'fallback')).toBe('Validation failed');
    expect(errorMessage(blank, 'fallback')).toBe('Validation failed');
  });
});

describe('validationErrors', () => {
  const error = new ApiError(400, {
    title: 'Validation failed',
    errors: {
      TimeZoneId: ['Time zone is not recognized.'],
      'custom:2f1c9d40-0000-0000-0000-000000000001': ['Company is required.'],
      'Days[3].Intervals': ['Intervals within a single day must not overlap.'],
    },
  });

  it('finds a field’s messages regardless of key casing', () => {
    // The API sends PascalCase today because DictionaryKeyPolicy is unset.
    // Matching case-insensitively means setting it would not silently drop
    // every field message.
    expect(validationErrors(error).for('TimeZoneId')).toEqual(['Time zone is not recognized.']);
    expect(validationErrors(error).for('timeZoneId')).toEqual(['Time zone is not recognized.']);
  });

  it('finds a custom booking question by the same key the tracker reports answers under', () => {
    expect(validationErrors(error).for('custom:2F1C9D40-0000-0000-0000-000000000001'))
      .toEqual(['Company is required.']);
  });

  it('returns nothing for a field the server said nothing about', () => {
    expect(validationErrors(error).for('Label')).toEqual([]);
  });

  it('reports everything no field claimed, so nothing is silently dropped', () => {
    const rest = validationErrors(error).unmapped(['TimeZoneId']);

    expect(rest).toContain('Intervals within a single day must not overlap.');
    expect(rest).toContain('Company is required.');
    expect(rest).not.toContain('Time zone is not recognized.');
  });

  it('is empty for anything that is not a validation rejection', () => {
    expect(validationErrors(new NetworkError()).hasAny).toBe(false);
    expect(validationErrors(new ApiError(404, { title: 'Not found.' })).hasAny).toBe(false);
    expect(validationErrors(null).hasAny).toBe(false);
    expect(validationErrors(new ApiError(400, { title: 'x', errors: {} })).hasAny).toBe(false);
  });
});

describe('availability', () => {
  const schedule = (days: WorkingScheduleDto['days']): WorkingScheduleDto => ({
    id: 's', organizerId: 'o', timeZoneId: 'UTC', days,
  });

  it('is bookable when at least one enabled day has an interval', () => {
    expect(hasBookableAvailability(schedule([
      { dayOfWeek: 1, isEnabled: true, intervals: [{ start: '09:00:00', end: '17:00:00' }] },
    ]))).toBe(true);
  });

  it('is not bookable when the only enabled day has no intervals', () => {
    expect(hasBookableAvailability(schedule([
      { dayOfWeek: 1, isEnabled: true, intervals: [] },
    ]))).toBe(false);
  });

  it('is not bookable when a day has intervals but is disabled', () => {
    expect(hasBookableAvailability(schedule([
      { dayOfWeek: 1, isEnabled: false, intervals: [{ start: '09:00:00', end: '17:00:00' }] },
    ]))).toBe(false);
  });

  it('is not bookable with no schedule at all', () => {
    expect(hasBookableAvailability(null)).toBe(false);
    expect(hasBookableAvailability(schedule([]))).toBe(false);
  });
});

describe('dayOfWeek', () => {
  it('names all seven days using .NET’s Sunday-zero numbering', () => {
    expect(DAY_NAMES[0]).toBe('Sunday');
    expect(DAY_NAMES[6]).toBe('Saturday');
    expect(Object.keys(DAY_NAMES)).toHaveLength(7);
  });

  it('displays the week Monday-first, covering every day exactly once', () => {
    expect(DAY_DISPLAY_ORDER[0]).toBe(1);
    expect(DAY_DISPLAY_ORDER[6]).toBe(0);
    expect(new Set(DAY_DISPLAY_ORDER).size).toBe(7);
  });
});
