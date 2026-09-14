using BookingTracker.Application.Notifications.Dtos;
using MediatR;

namespace BookingTracker.Application.Notifications.Queries.GetNotificationSettings;

public record GetNotificationSettingsQuery(Guid OrganizerId) : IRequest<NotificationSettingsDto>;
