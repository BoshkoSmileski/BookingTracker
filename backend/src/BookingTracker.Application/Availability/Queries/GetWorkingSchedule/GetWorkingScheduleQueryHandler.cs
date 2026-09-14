using BookingTracker.Application.Availability.Dtos;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace BookingTracker.Application.Availability.Queries.GetWorkingSchedule;

public class GetWorkingScheduleQueryHandler : IRequestHandler<GetWorkingScheduleQuery, WorkingScheduleDto?>
{
    private readonly IBookingTrackerDbContext _db;

    public GetWorkingScheduleQueryHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<WorkingScheduleDto?> Handle(GetWorkingScheduleQuery request, CancellationToken cancellationToken)
    {
        var schedule = await _db.WorkingSchedules
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizerId == request.OrganizerId, cancellationToken);

        if (schedule is null) return null;

        var days = await _db.WorkingDays
            .AsNoTracking()
            .Where(d => d.WorkingScheduleId == schedule.Id)
            .ToListAsync(cancellationToken);

        return schedule.ToDto(days);
    }
}
