using BookingTracker.Application.Calendar.Dtos;
using MediatR;

namespace BookingTracker.Application.Calendar.Commands.SyncNow;

public record SyncNowCommand(Guid OrganizerId) : IRequest<CalendarConnectionDto>;
