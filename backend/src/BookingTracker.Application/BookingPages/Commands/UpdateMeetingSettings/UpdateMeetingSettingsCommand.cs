using BookingTracker.Application.BookingPages.Dtos;
using BookingTracker.Domain.Enums;
using MediatR;

namespace BookingTracker.Application.BookingPages.Commands.UpdateMeetingSettings;

/// <summary>
/// Sets how bookings on this page meet (in person, or an online meeting the
/// system creates). Its own narrow command rather than an addition to
/// UpdateBookingPageDetailsCommand - the same call UpdateCalendarEventSettings
/// made against the sync-settings command it could have been folded into.
/// </summary>
public record UpdateMeetingSettingsCommand(
    Guid OrganizerId,
    Guid BookingPageId,
    MeetingProviderType MeetingProvider) : IRequest<BookingPageDetailDto>;
