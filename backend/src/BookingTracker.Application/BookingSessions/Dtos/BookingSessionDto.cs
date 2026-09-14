namespace BookingTracker.Application.BookingSessions.Dtos;

public record BookingSessionDto(
    Guid Id,
    Guid BookingPageId,
    string Status,
    string? Name,
    string? Email,
    string? Phone,
    string? Message,
    DateOnly? SelectedDate,
    TimeOnly? SelectedTime,
    DateTime CreatedAt,
    DateTime LastActivityAt,
    DateTime? SubmittedAt,
    DateTime? AbandonedAt,
    /// <summary>"GoogleMeet", or null when this booking has no online meeting.</summary>
    string? MeetingProvider,
    /// <summary>
    /// The join URL, or null. Read straight off the session - the one stored
    /// value every other surface (emails, the ICS attachment, the organizer's
    /// session detail, the guest's manage page) also reads, so none of them can
    /// show a different link.
    /// </summary>
    string? MeetingUrl,
    IReadOnlyList<BookingSessionAnswerDto> Answers);
