namespace BookingTracker.Application.Analytics.Export;

/// <summary>
/// One titled block of tabular data, already formatted as strings.
///
/// This is the single shape both exporters consume: the CSV writer emits it as
/// a section, the PDF writer draws it as a table. Formatting a value twice - once
/// per format - is how a percentage ends up rounded differently in the CSV than
/// in the PDF, so every cell is rendered exactly once, in
/// <see cref="AnalyticsReportTables"/>. Adding a section there makes it appear in
/// both exports; there is no per-format list to keep in step.
/// </summary>
/// <param name="Key">
/// Stable identity for the section. The PDF writer uses it to decide which
/// sections also get a chart drawn above the table - reading the numbers for
/// that chart from the analytics DTOs, never by parsing these formatted cells
/// back into numbers.
/// </param>
public record ReportTable(
    ReportTableKey Key,
    string Title,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows)
{
    /// <summary>A table with no rows still renders its heading and an explicit "no data" line - a silently missing section reads as a bug in the export.</summary>
    public bool IsEmpty => Rows.Count == 0;
}

/// <param name="Weight">Relative column width, used only by the PDF layout. The CSV ignores it.</param>
public record ReportColumn(string Header, ReportAlign Align = ReportAlign.Left, double Weight = 1);

public enum ReportAlign
{
    Left,
    Right,
}

public enum ReportTableKey
{
    Summary,
    Trend,
    StatusDistribution,
    Funnel,
    Abandonment,
    AbandonmentByStep,
    Weekdays,
    Hours,
    TimeToBook,
    PagePerformance,
    Email,
    EmailByType,
    Reminders,
    Calendar,
    Activity,
}
