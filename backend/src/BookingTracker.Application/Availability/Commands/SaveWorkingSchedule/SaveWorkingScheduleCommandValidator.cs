using BookingTracker.Domain.Common;
using FluentValidation;

namespace BookingTracker.Application.Availability.Commands.SaveWorkingSchedule;

public class SaveWorkingScheduleCommandValidator : AbstractValidator<SaveWorkingScheduleCommand>
{
    public SaveWorkingScheduleCommandValidator()
    {
        RuleFor(x => x.TimeZoneId)
            .NotEmpty()
            // Shared with the seeding path, which asks the same question and
            // falls back rather than rejecting - see WorkingScheduleDefaults.
            .Must(WorkingScheduleDefaults.IsKnownTimeZone)
            .WithMessage("'{PropertyValue}' is not a recognized IANA time zone id.");

        RuleFor(x => x.Days)
            .Must(days => days.Select(d => d.DayOfWeek).Distinct().Count() == days.Count)
            .WithMessage("Each day of the week may only appear once.");

        RuleForEach(x => x.Days).ChildRules(day =>
        {
            // The column is a tinyint (WorkingDayConfiguration's
            // HasConversion<byte>), so an out-of-range day is not rejected by
            // the database - it is silently truncated into one. Measured: -3
            // was accepted with 200, echoed back as -3, and stored and re-read
            // as 253; 300 became 44. The same boundary rule applied to an
            // enum rather than a length - the boundary rejects it so the column
            // never has to mangle it.
            day.RuleFor(d => d.DayOfWeek)
                .IsInEnum()
                .WithMessage("DayOfWeek must be a day of the week (0 = Sunday through 6 = Saturday).");

            day.RuleForEach(d => d.Intervals).ChildRules(interval =>
            {
                interval.RuleFor(i => i).Must(i => i.Start < i.End)
                    .WithMessage("Interval start must be before its end.");
            });

            day.RuleFor(d => d.Intervals)
                .Must(NotOverlap)
                .WithMessage("Intervals within a single day must not overlap.");
        });
    }

    private static bool NotOverlap(IReadOnlyList<Dtos.TimeRangeDto> intervals)
    {
        var ordered = intervals.OrderBy(i => i.Start).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Start < ordered[i - 1].End) return false;
        }
        return true;
    }
}
