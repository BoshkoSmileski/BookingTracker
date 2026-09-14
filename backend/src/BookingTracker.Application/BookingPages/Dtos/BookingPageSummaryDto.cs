namespace BookingTracker.Application.BookingPages.Dtos;

/// <summary>Backs the organizer's "all my booking pages" workspace list - richer than the public BookingPageDto, never exposed publicly.</summary>
public record BookingPageSummaryDto(
    Guid Id,
    string Slug,
    string Title,
    string? Description,
    bool IsActive,
    int DurationMinutes,
    int BufferBeforeMinutes,
    int BufferAfterMinutes,
    DateTime CreatedAt,
    int UpcomingBookingCount);
