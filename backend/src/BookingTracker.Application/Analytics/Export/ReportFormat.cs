using System.Globalization;

namespace BookingTracker.Application.Analytics.Export;

/// <summary>
/// How every exported value is turned into text. One place, so the CSV and the
/// PDF can never disagree on a rounding.
///
/// Everything is written with <see cref="CultureInfo.InvariantCulture"/>: an
/// export is data, and a decimal comma would make the file unreadable to any
/// consumer whose locale differs from the machine that produced it. Ratios are
/// widened to whole percentages (0.4286 -> "42.9") and their columns are headed
/// "(%)", because a bare 0.4286 in a spreadsheet is not a rate anyone reads.
/// </summary>
public static class ReportFormat
{
    public const string EmptyCell = "-";

    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A ratio in 0-1 rendered as a percentage magnitude ("42.9"), never with a % sign - the column header carries the unit so the cell stays numeric.</summary>
    public static string Percent(double? ratio, int decimals = 1) =>
        ratio is null ? EmptyCell : (ratio.Value * 100).ToString("F" + decimals, CultureInfo.InvariantCulture);

    public static string Decimal(double? value, int decimals = 1) =>
        value is null ? EmptyCell : value.Value.ToString("F" + decimals, CultureInfo.InvariantCulture);

    public static string Seconds(double? seconds) =>
        seconds is null ? EmptyCell : Math.Round(seconds.Value).ToString("F0", CultureInfo.InvariantCulture);

    /// <summary>
    /// Compact human duration - the same 45s / 3m 12s / 1h 4m shape
    /// components/analytics/chartTheme.ts renders on screen, so a reader
    /// comparing the report against the dashboard sees the same string.
    /// </summary>
    public static string Duration(double? seconds)
    {
        if (seconds is null) return EmptyCell;
        var total = Math.Max(0, (int)Math.Round(seconds.Value));
        if (total < 60) return $"{total}s";
        var minutes = total / 60;
        if (minutes < 60) return $"{minutes}m {total % 60}s";
        return $"{minutes / 60}h {minutes % 60}m";
    }

    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>ISO-8601 with an explicit Z, so a timestamp column is unambiguous wherever the file is opened.</summary>
    public static string Timestamp(DateTime? utc) =>
        utc is null
            ? EmptyCell
            : DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>Same instant, rendered for a human reading a printed page rather than for a parser.</summary>
    public static string ReadableTimestamp(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("dd MMM yyyy 'at' HH:mm 'UTC'", CultureInfo.InvariantCulture);

    public static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? EmptyCell : value;

    public static string Bool(bool value) => value ? "Yes" : "No";
}
