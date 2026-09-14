using BookingTracker.Application.Calendar.Dtos;
using MediatR;

namespace BookingTracker.Application.Calendar.Queries.GetCalendarConnection;

public record GetCalendarConnectionQuery(Guid OrganizerId) : IRequest<CalendarConnectionDto?>;
