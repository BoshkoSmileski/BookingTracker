import type { WorkingScheduleDto } from './types';

/** True only if the schedule has at least one enabled day with at least one time interval - i.e. guests could actually book something. */
export function hasBookableAvailability(schedule: WorkingScheduleDto | null): boolean {
  return !!schedule && schedule.days.some((d) => d.isEnabled && d.intervals.length > 0);
}
