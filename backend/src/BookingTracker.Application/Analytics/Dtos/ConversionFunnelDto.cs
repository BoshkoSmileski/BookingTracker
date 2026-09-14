namespace BookingTracker.Application.Analytics.Dtos;

/// <summary>
/// The conversion funnel and abandonment analysis, both derived from the
/// existing booking-session event log - no new tracking was introduced.
/// </summary>
public record ConversionFunnelDto(
    IReadOnlyList<FunnelStepDto> Steps,
    double ConversionRate,
    AbandonmentDto Abandonment);

/// <param name="Count">Distinct sessions that reached this step at least once, counted with COUNT(DISTINCT) in SQL.</param>
/// <param name="ShareOfEntry">Count relative to the first step, 0-1.</param>
/// <param name="StepConversion">Count relative to the previous step, 0-1 - where the drop-off actually happens.</param>
public record FunnelStepDto(string Step, int Count, double ShareOfEntry, double StepConversion);

/// <param name="AverageStepReached">Mean furthest step index (1-based, over the same steps as the funnel) for abandoned sessions.</param>
/// <param name="MostCommonStep">The step abandoned sessions most often got stuck on.</param>
public record AbandonmentDto(
    int TotalAbandoned,
    double AbandonmentRate,
    double? AverageStepReached,
    string? MostCommonStep,
    IReadOnlyList<AbandonmentStepDto> ByStep);

public record AbandonmentStepDto(string Step, int Count, double Share);
