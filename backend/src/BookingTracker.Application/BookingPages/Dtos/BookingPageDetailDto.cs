namespace BookingTracker.Application.BookingPages.Dtos;

/// <summary>Full organizer-owned view of a booking page - backs the create/edit settings pages.</summary>
public record BookingPageDetailDto(
    Guid Id,
    string Slug,
    string Title,
    string? Description,
    bool IsActive,
    int DurationMinutes,
    int BufferBeforeMinutes,
    int BufferAfterMinutes,
    int? MinNoticeMinutes,
    int? MaxBookingWindowDays,
    int? MaxBookingsPerDay,
    DateTime CreatedAt,
    /// <summary>"None" or "GoogleMeet" - a string like every other enum on this API.</summary>
    string MeetingProvider,
    IReadOnlyList<BookingInstructionDto> Instructions,
    IReadOnlyList<BookingFormFieldDto> FormFields);
