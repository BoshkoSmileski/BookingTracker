using BookingTracker.Application.Notifications.Dtos;
using MediatR;

namespace BookingTracker.Application.Notifications.Queries.GetBookingReminders;

public record GetBookingRemindersQuery(Guid SessionId, Guid RequestingOrganizerId) : IRequest<IReadOnlyList<BookingReminderDto>>;
