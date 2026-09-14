namespace BookingTracker.Application.Analytics.Export;

/// <summary>
/// Colours for the PDF report.
///
/// These are the exact values in frontend/src/components/analytics/chartTheme.ts,
/// mirrored by hand the same way lib/types.ts mirrors the backend DTOs - so a
/// printed report and the dashboard it was exported from are recognisably the
/// same document. The validated properties that palette carries (fixed
/// categorical slot order, single-hue sequential ramp, adjacent-pair separation
/// for colour-vision deficiency) hold here only because the values and the slot
/// order are copied unchanged: assign by index, never cycle, never re-order.
///
/// Print adds one constraint the screen does not have - a report may well be
/// read in greyscale - which is why every chart in AnalyticsPdfWriter labels its
/// values as text rather than relying on colour to carry meaning.
/// </summary>
public static class AnalyticsReportTheme
{
    /// <summary>Fixed categorical order. Slot 0 is sessions/primary, slot 1 is the second series.</summary>
    public static readonly string[] Series = ["#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4"];

    /// <summary>Single-hue ramp for magnitude. Light to dark = low to high.</summary>
    public static readonly string[] Sequential = ["#86b6ef", "#5598e7", "#3987e5", "#2a78d6", "#256abf", "#184f95"];

    public const string InkPrimary = "#0b0b0b";
    public const string InkSecondary = "#52514e";
    public const string InkMuted = "#898781";
    public const string Grid = "#e1e0d9";
    public const string Axis = "#c3c2b7";
    public const string Surface = "#ffffff";

    /// <summary>Panel/table-header fill and the alternating row tint. Lighter than Grid so a rule still reads against them.</summary>
    public const string PanelFill = "#f6f6f3";
    public const string ZebraFill = "#fbfbf9";

    /// <summary>Status keeps the same slot it has on screen, so a doughnut slice and a printed bar are the same colour.</summary>
    public static string StatusColor(string status) => status switch
    {
        "Upcoming" => Series[0],
        "Completed" => Series[2],
        "Cancelled" => Series[1],
        "Abandoned" => Series[4],
        "In progress" => Series[3],
        _ => InkMuted,
    };

    /// <summary>Picks a ramp step by rank so the tallest bar is darkest - magnitude read twice, by length and by depth.</summary>
    public static string SequentialStep(double value, double max)
    {
        if (max <= 0) return Sequential[0];
        var index = (int)Math.Round(value / max * (Sequential.Length - 1));
        return Sequential[Math.Clamp(index, 0, Sequential.Length - 1)];
    }
}
