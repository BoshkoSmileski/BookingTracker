using BookingTracker.Application.BookingPages.Dtos;
using MediatR;

namespace BookingTracker.Application.BookingPages.Commands.UpdateBookingPageLimits;

public record UpdateBookingPageLimitsCommand(
    Guid OrganizerId,
    Guid BookingPageId,
    int? MinNoticeMinutes,
    int? MaxBookingWindowDays,
    int? MaxBookingsPerDay) : IRequest<BookingPageDetailDto>;
