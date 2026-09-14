using BookingTracker.Application.Availability.Dtos;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.ValueObjects;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.Availability.Commands.SaveWorkingSchedule;

public class SaveWorkingScheduleCommandHandler : IRequestHandler<SaveWorkingScheduleCommand, WorkingScheduleDto>
{
    private readonly IBookingTrackerDbContext _db;

    public SaveWorkingScheduleCommandHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<WorkingScheduleDto> Handle(SaveWorkingScheduleCommand request, CancellationToken cancellationToken)
    {
        var schedule = await _db.WorkingSchedules.FirstOrDefaultAsync(s => s.OrganizerId == request.OrganizerId, cancellationToken);

        if (schedule is null)
        {
            schedule = WorkingSchedule.Create(request.OrganizerId, request.TimeZoneId);
            _db.WorkingSchedules.Add(schedule);
        }
        else
        {
            schedule.ChangeTimeZone(request.TimeZoneId);

            var existingDays = await _db.WorkingDays
                .Where(d => d.WorkingScheduleId == schedule.Id)
                .ToListAsync(cancellationToken);
            _db.WorkingDays.RemoveRange(existingDays);
        }

        var newDays = request.Days
            .Select(d => WorkingDay.Create(
                schedule.Id,
                d.DayOfWeek,
                d.IsEnabled,
                d.Intervals.Select(i => TimeRange.Create(i.Start, i.End))))
            .ToList();

        _db.WorkingDays.AddRange(newDays);
        await _db.SaveChangesAsync(cancellationToken);

        return schedule.ToDto(newDays);
    }
}
