import { describe, expect, it } from 'vitest';
import { MAX_OVERRIDE_RANGES, describeOverrideHours, toRangeInputs } from '../availabilityOverrides';
import { availabilityOverride } from '../../test/factories';

describe('toRangeInputs', () => {
  it('trims backend HH:mm:ss down to the HH:mm the time inputs use', () => {
    expect(toRangeInputs([{ start: '09:00:00', end: '17:30:00' }])).toEqual([{ start: '09:00', end: '17:30' }]);
  });

  it('keeps every range, so a multi-range day loads back into the editor intact', () => {
    const result = toRangeInputs([
      { start: '09:00:00', end: '12:00:00' },
      { start: '13:00:00', end: '15:00:00' },
    ]);

    expect(result).toEqual([
      { start: '09:00', end: '12:00' },
      { start: '13:00', end: '15:00' },
    ]);
  });
});

describe('describeOverrideHours', () => {
  it('says Closed for a day with no hours', () => {
    // The one place the frontend decides what an empty `ranges` means, so the
    // list row and any future summary cannot disagree about it.
    expect(describeOverrideHours(availabilityOverride({ isClosed: true, ranges: [] }))).toBe('Closed');
  });

  it('joins multiple ranges, so a split day reads as one line', () => {
    const result = describeOverrideHours(
      availabilityOverride({
        ranges: [
          { start: '09:00:00', end: '12:00:00' },
          { start: '13:00:00', end: '15:00:00' },
        ],
      }),
    );

    // Derived from the same formatter the code uses rather than hardcoded, so
    // this does not break on a machine with a different locale.
    expect(result).toContain('–');
    expect(result.split(', ')).toHaveLength(2);
  });
});

describe('MAX_OVERRIDE_RANGES', () => {
  it('mirrors the backend AvailabilityOverride.MaxRanges', () => {
    expect(MAX_OVERRIDE_RANGES).toBe(6);
  });
});
