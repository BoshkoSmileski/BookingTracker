namespace BookingTracker.Application.Analytics.Dtos;

/// <summary>
/// Everything the analytics dashboard shows, in one object, so an export can be
/// rendered from exactly the same numbers the screen renders.
///
/// It carries the four existing panel DTOs unchanged rather than a flattened
/// copy of them: the export formatters read the same records the dashboard does,
/// so there is no second place where a metric could be computed (or rounded)
/// differently. Adding a field to a panel DTO reaches the export for free.
/// </summary>
public record AnalyticsReportDto(
    AnalyticsReportMetaDto Meta,
    BookingAnalyticsDto Bookings,
    ConversionFunnelDto Funnel,
    OperationalAnalyticsDto Operations,
    IReadOnlyList<ActivityEntryDto> Activity);

/// <summary>
/// Report-level context a reader needs to interpret the numbers: who they belong
/// to, when they were produced, and which filter produced them.
///
/// The three <c>*Label</c> values are resolved server-side (a booking page id
/// becomes its title, an absent status becomes "All statuses") because both
/// formatters need the same wording, and re-deriving it in each would let the
/// CSV and the PDF describe the same export differently.
/// </summary>
public record AnalyticsReportMetaDto(
    string OrganizerName,
    string OrganizerEmail,
    DateTime GeneratedAtUtc,
    DateOnly? From,
    DateOnly? To,
    string DateRangeLabel,
    string BookingPageLabel,
    string StatusLabel,
    string TimeZoneId);
