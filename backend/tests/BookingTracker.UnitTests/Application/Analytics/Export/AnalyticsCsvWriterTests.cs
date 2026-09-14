using System.Text;
using BookingTracker.Application.Analytics.Dtos;
using BookingTracker.Application.Analytics.Export;
using BookingTracker.UnitTests.TestSupport;

namespace BookingTracker.UnitTests.Application.Analytics.Export;

/// <summary>
/// The CSV writer is a pure function of a report, so these hand it one directly.
/// They cover the three things a CSV export has to get right and that nothing
/// else in the system would catch: the RFC 4180 mechanics, the encoding
/// preamble Excel needs, and the formula guard on user-supplied text.
/// </summary>
public class AnalyticsCsvWriterTests
{
    private static string[] Lines(string csv) => csv.Split("\r\n");

    private static string Csv(AnalyticsReportDto report) => AnalyticsCsvWriter.WriteText(report);

    [Fact]
    public void EmitsEverySectionTheReportModelDefines()
    {
        var csv = Csv(AnalyticsReports.Populated());

        // Every section AnalyticsReportTables produces must appear - the CSV is
        // the export that is meant to be complete rather than presentable.
        foreach (var expected in new[]
        {
            "Summary", "Booking trend", "Status distribution", "Conversion funnel",
            "Abandonment", "Abandonment by step", "Bookings by weekday",
            "Bookings by hour (Europe/Skopje)", "Time to book", "Booking page performance",
            "Email delivery", "Email by notification type", "Reminders", "Calendar sync",
            "Recent activity",
        })
        {
            Assert.Contains(Lines(csv), l => l == expected);
        }
    }

    [Fact]
    public void OpensWithTheOrganizerAndTheAppliedFilter()
    {
        var csv = Csv(AnalyticsReports.Populated(
            AnalyticsReports.Meta(bookingPageLabel: "Discovery Call", statusLabel: "Submitted")));
        var lines = Lines(csv);

        Assert.Equal("BookingTracker analytics export", lines[0]);
        Assert.Contains(lines, l => l == "Organizer,Test Organizer");
        Assert.Contains(lines, l => l == "Date range,2026-07-06 to 2026-08-04");
        Assert.Contains(lines, l => l == "Booking page,Discovery Call");
        Assert.Contains(lines, l => l == "Status,Submitted");
        Assert.Contains(lines, l => l == "Generated (UTC),2026-08-05T09:30:00Z");
    }

    [Fact]
    public void UsesCrlfLineEndingsThroughout()
    {
        var csv = Csv(AnalyticsReports.Populated());

        // RFC 4180 requires CRLF, and a bare LF anywhere is what makes a CSV
        // arrive as one long row in some readers.
        var withoutLineBreaks = csv.Replace("\r\n", string.Empty);
        Assert.DoesNotContain("\n", withoutLineBreaks, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", withoutLineBreaks, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteStartsWithTheUtf8ByteOrderMark()
    {
        var bytes = AnalyticsCsvWriter.Write(AnalyticsReports.Populated());

        // Without it Excel reads the file as the machine's ANSI code page and any
        // non-ASCII title arrives mojibake.
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes.Take(3));
    }

    [Fact]
    public void PreservesNonAsciiTextExactly()
    {
        var report = AnalyticsReports.Populated(AnalyticsReports.Meta(organizerName: "Bosko Smileski"));
        var withCyrillic = report with { Meta = report.Meta with { OrganizerName = "Бошко Смилески" } };

        var text = Encoding.UTF8.GetString(AnalyticsCsvWriter.Write(withCyrillic));

        // The CSV is UTF-8 and carries any script intact - unlike the PDF, whose
        // base-14 fonts cannot (see AnalyticsPdfWriterTests).
        Assert.Contains("Бошко Смилески", text, StringComparison.Ordinal);
    }

    [Fact]
    public void QuotesFieldsContainingCommasQuotesOrNewlines()
    {
        var report = AnalyticsReports.Populated(pages:
        [
            new BookingPagePerformanceDto(
                Guid.NewGuid(), "Coffee, cake & a \"quick\" chat", "coffee", true, 10, 4, 1, 3, 0.4, 0.2),
        ]);

        var line = Assert.Single(Lines(Csv(report)), l => l.StartsWith("\"Coffee,", StringComparison.Ordinal));

        // Embedded quotes are doubled, not escaped with a backslash.
        Assert.StartsWith("\"Coffee, cake & a \"\"quick\"\" chat\",coffee,Yes,", line);
    }

    [Fact]
    public void NeutralisesTextThatWouldBeReadAsASpreadsheetFormula()
    {
        var report = AnalyticsReports.Populated(activity:
        [
            new ActivityEntryDto("EmailFailed", "=HYPERLINK(\"http://evil\",\"click\")", AnalyticsReports.GeneratedAt, null, null),
            new ActivityEntryDto("EmailSent", "@SUM(A1:A9)", AnalyticsReports.GeneratedAt, null, null),
        ]);

        var csv = Csv(report);

        // Activity descriptions carry visitor- and organizer-supplied text, so a
        // leading = or @ has to be pinned as text or the spreadsheet evaluates it.
        Assert.Contains("'=HYPERLINK", csv, StringComparison.Ordinal);
        Assert.Contains("'@SUM(A1:A9)", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void LeavesNumbersAndThePlaceholderAlone()
    {
        var csv = Csv(AnalyticsReports.Empty());

        // The formula guard must never turn a numeric column into text, and the
        // "-" placeholder is not a formula either.
        Assert.Contains(Lines(csv), l => l == "Average furthest step reached,-");
        Assert.DoesNotContain("'-", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("'0", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void WritesRatesAsPercentageMagnitudesUnderALabelledColumn()
    {
        var csv = Csv(AnalyticsReports.Populated());

        // 0.4286 is not a rate anyone reads; the header carries the unit so the
        // cell can stay a plain number.
        Assert.Contains(Lines(csv), l => l == "Completion rate (%),40.0");
        Assert.Contains(Lines(csv), l => l.StartsWith("30 Minute Meeting,demo-30-min-meeting,Yes,14,6,42.9,2,25.0,4", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryRowInASectionHasTheSameColumnCountAsItsHeader()
    {
        var csv = Csv(AnalyticsReports.Populated());
        var titles = AnalyticsReportTables.Build(AnalyticsReports.Populated()).Select(t => t.Title).ToHashSet();

        var lines = Lines(csv);
        var expectedColumns = 0;

        foreach (var line in lines)
        {
            if (line.Length == 0) { expectedColumns = 0; continue; }
            if (titles.Contains(line)) { expectedColumns = -1; continue; }   // next line is the header
            if (expectedColumns == -1) { expectedColumns = CountFields(line); continue; }
            if (expectedColumns > 0) Assert.Equal(expectedColumns, CountFields(line));
        }
    }

    [Fact]
    public void AnEmptyReportStillWritesEverySectionAndSaysItIsEmpty()
    {
        var csv = Csv(AnalyticsReports.Empty());
        var lines = Lines(csv);

        // A section that silently vanishes is indistinguishable from one the
        // export forgot to write.
        Assert.Contains(lines, l => l == "Recent activity");
        Assert.Contains(lines, l => l == "No data in this range");
        Assert.Contains(lines, l => l == "Visitors (sessions started),0");
    }

    [Fact]
    public void HandlesAYearOfDailyTrendPointsWithoutLosingRows()
    {
        var report = AnalyticsReports.Large(trendDays: 365, activityEntries: 100);
        var lines = Lines(Csv(report));

        var start = Array.IndexOf(lines, "Booking trend");
        Assert.True(start >= 0);

        // Title, header, then exactly one row per day - nothing dropped, nothing
        // paginated away. Expectations come from the report rather than being
        // written down, so the assertion is about the writer, not about arithmetic.
        Assert.Equal("Date,Sessions,Bookings", lines[start + 1]);
        var trend = report.Bookings.Trend;
        Assert.Equal($"{trend[0].Date:yyyy-MM-dd},{trend[0].Sessions},{trend[0].Bookings}", lines[start + 2]);
        Assert.Equal($"{trend[^1].Date:yyyy-MM-dd},{trend[^1].Sessions},{trend[^1].Bookings}", lines[start + 1 + trend.Count]);
    }

    /// <summary>Counts RFC 4180 fields, so a quoted comma is not miscounted as a separator.</summary>
    private static int CountFields(string line)
    {
        var fields = 1;
        var inQuotes = false;
        foreach (var ch in line)
        {
            if (ch == '"') inQuotes = !inQuotes;
            else if (ch == ',' && !inQuotes) fields++;
        }
        return fields;
    }
}
