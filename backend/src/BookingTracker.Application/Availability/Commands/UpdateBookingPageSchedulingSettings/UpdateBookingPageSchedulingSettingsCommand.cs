using BookingTracker.Application.BookingPages.Dtos;
using MediatR;

namespace BookingTracker.Application.Availability.Commands.UpdateBookingPageSchedulingSettings;

public record UpdateBookingPageSchedulingSettingsCommand(
    Guid OrganizerId,
    Guid BookingPageId,
    int DurationMinutes,
    int BufferBeforeMinutes,
    int BufferAfterMinutes) : IRequest<BookingPageDto>;
