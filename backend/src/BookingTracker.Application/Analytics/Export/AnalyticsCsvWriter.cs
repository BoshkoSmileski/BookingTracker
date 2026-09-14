using System.Globalization;
using System.Text;
using BookingTracker.Application.Analytics.Dtos;

namespace BookingTracker.Application.Analytics.Export;

/// <summary>
/// Renders an analytics report as CSV. Static and pure - it takes a report and
/// returns bytes, with no I/O and no knowledge of HTTP.
///
/// Written to RFC 4180: comma-delimited, CRLF line endings, and a field quoted
/// only when it contains a comma, a quote, a line break or edge whitespace, with
/// embedded quotes doubled. A UTF-8 BOM is prepended because that is the one
/// thing that makes Excel open a UTF-8 CSV as UTF-8 instead of as the machine's
/// ANSI code page - without it, any non-ASCII booking page title arrives mojibake.
///
/// Numbers are written with the invariant culture and rate columns carry their
/// unit in the header ("Conversion (%)") rather than a % sign in the cell, so
/// every numeric column stays numeric to a parser. See ReportFormat.
/// </summary>
public static class AnalyticsCsvWriter
{
    public const string ContentType = "text/csv";

    /// <summary>Excel treats a leading one of these as the start of a formula, so a value beginning with one is prefixed with an apostrophe unless it is plainly a number.</summary>
    private static readonly char[] FormulaTriggers = ['=', '+', '-', '@', '\t', '\r'];

    private const string NewLine = "\r\n";

    /// <summary>UTF-8 with BOM - see the type remarks for why the BOM is not optional here.</summary>
    public static byte[] Write(AnalyticsReportDto report)
    {
        var text = WriteText(report);
        var bytes = Encoding.UTF8.GetBytes(text);
        return [.. Encoding.UTF8.GetPreamble(), .. bytes];
    }

    /// <summary>The same document without the BOM, so tests can assert on content rather than on an encoding preamble.</summary>
    public static string WriteText(AnalyticsReportDto report)
    {
        var builder = new StringBuilder();

        WriteHeaderBlock(builder, report.Meta);

        foreach (var table in AnalyticsReportTables.Build(report))
        {
            WriteTable(builder, table);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Who, when, and under which filter - written before any figures, because a
    /// column of numbers detached from its window cannot be interpreted six
    /// months later.
    /// </summary>
    private static void WriteHeaderBlock(StringBuilder builder, AnalyticsReportMetaDto meta)
    {
        WriteRow(builder, ["BookingTracker analytics export"]);
        WriteRow(builder, ["Organizer", meta.OrganizerName]);
        WriteRow(builder, ["Organizer email", meta.OrganizerEmail]);
        WriteRow(builder, ["Generated (UTC)", ReportFormat.Timestamp(meta.GeneratedAtUtc)]);
        WriteRow(builder, ["Date range", meta.DateRangeLabel]);
        WriteRow(builder, ["Booking page", meta.BookingPageLabel]);
        WriteRow(builder, ["Status", meta.StatusLabel]);
        WriteRow(builder, ["Time zone", meta.TimeZoneId]);
    }

    private static void WriteTable(StringBuilder builder, ReportTable table)
    {
        // Blank line before each section: the conventional way to make a
        // multi-section CSV readable, and what lets a reader (or Excel's
        // "From Text/CSV" import) see where one grid stops and the next starts.
        builder.Append(NewLine);
        WriteRow(builder, [table.Title]);
        WriteRow(builder, table.Columns.Select(c => c.Header).ToList());

        if (table.IsEmpty)
        {
            // An empty section still appears, saying so - a section that silently
            // vanishes is indistinguishable from one the export forgot to write.
            WriteRow(builder, ["No data in this range"]);
            return;
        }

        foreach (var row in table.Rows)
        {
            WriteRow(builder, row);
        }
    }

    private static void WriteRow(StringBuilder builder, IReadOnlyList<string> cells)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0) builder.Append(',');
            builder.Append(Escape(cells[i]));
        }
        builder.Append(NewLine);
    }

    /// <summary>
    /// RFC 4180 quoting plus a spreadsheet-formula guard.
    ///
    /// Booking page titles and activity descriptions contain visitor- and
    /// organizer-supplied text, so a cell can legitimately start with "=" or "@".
    /// Excel and LibreOffice would evaluate that as a formula on open, which is
    /// the CSV-injection vector; prefixing an apostrophe pins it as text. Plain
    /// numbers (including negatives) and the "-" placeholder are exempt, so the
    /// guard never turns a numeric column into text.
    /// </summary>
    private static string Escape(string? value)
    {
        var cell = value ?? string.Empty;

        if (NeedsFormulaGuard(cell)) cell = "'" + cell;

        var mustQuote = cell.Length > 0
            && (cell.IndexOfAny([',', '"', '\r', '\n']) >= 0
                || char.IsWhiteSpace(cell[0])
                || char.IsWhiteSpace(cell[^1]));

        return mustQuote ? '"' + cell.Replace("\"", "\"\"", StringComparison.Ordinal) + '"' : cell;
    }

    private static bool NeedsFormulaGuard(string cell)
    {
        if (cell.Length == 0 || Array.IndexOf(FormulaTriggers, cell[0]) < 0) return false;
        if (cell == ReportFormat.EmptyCell) return false;
        return !double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }
}
