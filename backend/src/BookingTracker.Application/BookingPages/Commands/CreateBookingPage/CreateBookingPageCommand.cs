using BookingTracker.Application.BookingPages.Dtos;
using MediatR;

namespace BookingTracker.Application.BookingPages.Commands.CreateBookingPage;

/// <param name="TimeZoneId">
/// The organizer's IANA zone, as resolved by their browser. A *hint*, used for
/// one thing only: if this organizer has no working schedule yet, creating this
/// page seeds a default one and this decides which clock it is on. Ignored
/// entirely once a schedule exists, and an unrecognized value falls back to UTC
/// rather than failing the request - see WorkingScheduleDefaults.
/// </param>
public record CreateBookingPageCommand(
    Guid OrganizerId,
    string Title,
    string? Description,
    int DurationMinutes,
    int BufferBeforeMinutes,
    int BufferAfterMinutes,
    int? MinNoticeMinutes,
    int? MaxBookingWindowDays,
    int? MaxBookingsPerDay,
    string? TimeZoneId = null) : IRequest<BookingPageDetailDto>;
