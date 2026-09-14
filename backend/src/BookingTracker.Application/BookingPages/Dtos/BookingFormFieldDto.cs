namespace BookingTracker.Application.BookingPages.Dtos;

/// <summary>
/// One organizer-defined question a visitor answers while booking.
///
/// Carried by both the public <see cref="BookingPageDto"/> (so the wizard can
/// render the inputs) and the organizer's <see cref="BookingPageDetailDto"/>
/// (so the editor can list them) - the same ordered projection in both, via
/// BookingPageMappings.ToFormFieldDtos.
/// </summary>
/// <param name="Type">"ShortText" or "LongText" - which input control to render, nothing more.</param>
public record BookingFormFieldDto(
    Guid Id,
    string Label,
    string Type,
    bool IsRequired,
    int DisplayOrder);
