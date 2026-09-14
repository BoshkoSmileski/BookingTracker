using BookingTracker.Application.Availability.Dtos;
using BookingTracker.Application.Common.Interfaces;
using BookingTracker.Application.Common.Mappings;
using BookingTracker.Domain.Entities;
using BookingTracker.Domain.Enums;
using MediatR;

namespace BookingTracker.Application.Availability.Commands.CreateAvailabilityException;

public class CreateAvailabilityExceptionCommandHandler
    : IRequestHandler<CreateAvailabilityExceptionCommand, AvailabilityExceptionDto>
{
    private readonly IBookingTrackerDbContext _db;

    public CreateAvailabilityExceptionCommandHandler(IBookingTrackerDbContext db) => _db = db;

    public async Task<AvailabilityExceptionDto> Handle(CreateAvailabilityExceptionCommand request, CancellationToken cancellationToken)
    {
        var type = Enum.Parse<AvailabilityExceptionType>(request.Type, ignoreCase: true);

        var exception = AvailabilityException.Create(
            request.OrganizerId, request.Date, request.StartTime, request.EndTime, type, request.Reason, request.EndDate);

        _db.AvailabilityExceptions.Add(exception);
        await _db.SaveChangesAsync(cancellationToken);

        return exception.ToDto();
    }
}
