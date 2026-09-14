using BookingTracker.Application.BookingPages.Dtos;
using BookingTracker.Domain.Entities;

namespace BookingTracker.Application.Common.Mappings;

public static class BookingPageMappings
{
    /// <summary>
    /// OrganizerName and TimeZoneId aren't on BookingPage itself (aggregates
    /// reference each other by id only, per this codebase's convention), so
    /// callers must supply them - typically from a join against Organizers and
    /// WorkingSchedules respectively.
    /// </summary>
    /// <param name="timeZoneId">
    /// The organizer's <c>WorkingSchedule.TimeZoneId</c>. Required rather than
    /// defaulted: a silently-"UTC" time zone label beside organizer-local times
    /// is the same class of bug as the review step that used to name the
    /// visitor's zone beside the organizer's clock. Pass "UTC" explicitly when
    /// there genuinely is no schedule - such a page has no slots to label either.
    /// </param>
    public static BookingPageDto ToDto(this BookingPage page, string organizerName, string timeZoneId) => new(
        page.Id,
        page.Slug,
        page.Title,
        page.Description,
        organizerName,
        page.DurationMinutes,
        page.BufferBeforeMinutes,
        page.BufferAfterMinutes,
        page.MeetingProvider.ToString(),
        timeZoneId,
        page.ToInstructionDtos(),
        page.ToFormFieldDtos());

    public static BookingPageSummaryDto ToSummaryDto(this BookingPage page, int upcomingBookingCount) => new(
        page.Id,
        page.Slug,
        page.Title,
        page.Description,
        page.IsActive,
        page.DurationMinutes,
        page.BufferBeforeMinutes,
        page.BufferAfterMinutes,
        page.CreatedAt,
        upcomingBookingCount);

    public static BookingPageDetailDto ToDetailDto(this BookingPage page) => new(
        page.Id,
        page.Slug,
        page.Title,
        page.Description,
        page.IsActive,
        page.DurationMinutes,
        page.BufferBeforeMinutes,
        page.BufferAfterMinutes,
        page.MinNoticeMinutes,
        page.MaxBookingWindowDays,
        page.MaxBookingsPerDay,
        page.CreatedAt,
        page.MeetingProvider.ToString(),
        page.ToInstructionDtos(),
        page.ToFormFieldDtos());

    /// <summary>
    /// Ordered instructions for a page. One helper rather than a repeated
    /// OrderBy at each call site, so the public booking page and the organizer's
    /// editor can never present them in a different order.
    /// </summary>
    public static IReadOnlyList<BookingInstructionDto> ToInstructionDtos(this BookingPage page) =>
        page.Questions.OrderBy(q => q.DisplayOrder).Select(q => q.ToDto()).ToList();

    /// <summary>BookingQuestion is the Domain name; the DTO says instruction, which is what it is.</summary>
    public static BookingInstructionDto ToDto(this BookingQuestion question) => new(
        question.Id,
        question.Prompt,
        question.DisplayOrder);

    /// <summary>
    /// Ordered custom form fields for a page - the same helper reasoning as
    /// ToInstructionDtos above: the order a visitor answers them in and the
    /// order the organizer edits them in must be one decision, not two.
    /// </summary>
    public static IReadOnlyList<BookingFormFieldDto> ToFormFieldDtos(this BookingPage page) =>
        page.FormFields.OrderBy(f => f.DisplayOrder).Select(f => f.ToDto()).ToList();

    public static BookingFormFieldDto ToDto(this BookingFormField field) => new(
        field.Id,
        field.Label,
        field.Type.ToString(),
        field.IsRequired,
        field.DisplayOrder);
}
