using BookingTracker.Application.Analytics.Dtos;
using BookingTracker.Application.Analytics.Export;
using BookingTracker.UnitTests.TestSupport;

namespace BookingTracker.UnitTests.Application.Analytics.Export;

/// <summary>
/// The PDF is written byte by byte in this repository, so "it produced some
/// bytes" is not a test. These read the document back with
/// <see cref="PdfInspector"/> and assert on what a reader would actually find:
/// a valid cross-reference table, the right number of pages, and the report's
/// own numbers as drawn text.
/// </summary>
public class AnalyticsPdfWriterTests
{
    private static byte[] Pdf(AnalyticsReportDto report) => AnalyticsPdfWriter.Write(report);

    [Fact]
    public void ProducesAStructurallyValidDocument()
    {
        var pdf = Pdf(AnalyticsReports.Populated());

        // Includes the check that startxref really points at the xref table - an
        // off-by-one there is invisible until a reader refuses the file.
        Assert.True(PdfInspector.IsStructurallyValid(pdf, out var reason), reason);
    }

    [Fact]
    public void DeclaredPageCountMatchesThePageObjectsPresent()
    {
        var pdf = Pdf(AnalyticsReports.Populated());

        Assert.Equal(PdfInspector.PageObjectCount(pdf), PdfInspector.DeclaredPageCount(pdf));
        Assert.True(PdfInspector.DeclaredPageCount(pdf) > 1);
    }

    [Fact]
    public void CarriesTheTitleTheGenerationTimestampAndTheAppliedFilter()
    {
        var text = PdfInspector.AllText(Pdf(AnalyticsReports.Populated(
            AnalyticsReports.Meta(bookingPageLabel: "Discovery Call", statusLabel: "Submitted"))));

        Assert.Contains("Analytics report", text, StringComparison.Ordinal);
        Assert.Contains("Test Organizer", text, StringComparison.Ordinal);
        Assert.Contains("2026-07-06 to 2026-08-04", text, StringComparison.Ordinal);
        Assert.Contains("Discovery Call", text, StringComparison.Ordinal);
        Assert.Contains("Submitted", text, StringComparison.Ordinal);
        Assert.Contains("Europe/Skopje", text, StringComparison.Ordinal);
        Assert.Contains("05 Aug 2026 at 09:30 UTC", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NumbersEveryPageWithTheTotal()
    {
        var pdf = Pdf(AnalyticsReports.Populated());
        var runs = PdfInspector.TextRuns(pdf);
        var total = PdfInspector.DeclaredPageCount(pdf);

        for (var page = 1; page <= total; page++)
        {
            Assert.Contains($"Page {page} of {total}", runs);
        }
    }

    [Fact]
    public void RendersEverySectionHeading()
    {
        var text = PdfInspector.AllText(Pdf(AnalyticsReports.Populated()));

        foreach (var table in AnalyticsReportTables.Build(AnalyticsReports.Populated()))
        {
            Assert.Contains(table.Title, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void RendersTheSummaryAsStatCardsAndAsATable()
    {
        var runs = PdfInspector.TextRuns(Pdf(AnalyticsReports.Populated()));

        // The card grid uses upper-cased labels; the table below repeats the same
        // figures in full wording, which is what makes the report readable both
        // at a glance and in detail.
        Assert.Contains("VISITORS", runs);
        Assert.Contains("Visitors (sessions started)", runs);
        Assert.Contains("20", runs);
        Assert.Contains("40%", runs);
    }

    [Fact]
    public void ChartsLabelTheirValuesAsTextRatherThanRelyingOnColour()
    {
        var runs = PdfInspector.TextRuns(Pdf(AnalyticsReports.Populated()));

        // A report may be printed in greyscale, so every bar carries its number
        // and the trend chart carries a legend and axis labels.
        Assert.Contains("Sessions", runs);
        Assert.Contains("Bookings", runs);
        Assert.Contains("2026-08-01", runs);
        Assert.Contains("2026-08-04", runs);
        foreach (var weekday in new[] { "Monday", "Tuesday", "Saturday" })
        {
            Assert.Contains(weekday, runs);
        }
    }

    [Fact]
    public void ExportsTheSameFormattedValuesAsTheCsv()
    {
        var report = AnalyticsReports.Populated();
        var runs = PdfInspector.TextRuns(Pdf(report));

        // REGRESSION: both formats render the shared report model, so a cell can
        // never be rounded one way on paper and another in the CSV. A cell too
        // wide for its column is drawn truncated with an ellipsis, which counts -
        // what must never happen is a *different* value.
        foreach (var table in AnalyticsReportTables.Build(report))
        {
            foreach (var cell in table.Rows.SelectMany(r => r))
            {
                Assert.True(
                    runs.Contains(cell) || runs.Any(r => IsTruncationOf(r, cell)),
                    $"'{cell}' from the '{table.Title}' table was not drawn in the PDF.");
            }
        }

        static bool IsTruncationOf(string drawn, string cell) =>
            drawn.EndsWith("...", StringComparison.Ordinal)
            && drawn.Length > 3
            && cell.StartsWith(drawn[..^3], StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyReportStillProducesAValidDocumentWithEverySection()
    {
        var pdf = Pdf(AnalyticsReports.Empty());

        Assert.True(PdfInspector.IsStructurallyValid(pdf, out var reason), reason);
        var text = PdfInspector.AllText(pdf);
        Assert.Contains("Recent activity", text, StringComparison.Ordinal);
        Assert.Contains("No data in this range.", text, StringComparison.Ordinal);
        Assert.Contains("Page 1 of", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ALargeReportPaginatesInsteadOfOverflowingOnePage()
    {
        var large = Pdf(AnalyticsReports.Large(trendDays: 365, activityEntries: 100));
        var small = Pdf(AnalyticsReports.Populated());

        Assert.True(PdfInspector.IsStructurallyValid(large, out var reason), reason);
        Assert.True(PdfInspector.DeclaredPageCount(large) > PdfInspector.DeclaredPageCount(small));
        // 365 trend rows plus 100 activity rows cannot fit in a handful of pages.
        Assert.True(PdfInspector.DeclaredPageCount(large) >= 10);
    }

    [Fact]
    public void RepeatsATableHeaderOnEveryPageItSpills()
    {
        var pdf = Pdf(AnalyticsReports.Large(trendDays: 365));
        var runs = PdfInspector.TextRuns(pdf);

        // "Date" is the trend table's first column and appears nowhere else, so
        // counting it counts how many pages that table labelled. Unlabelled
        // columns halfway through a report are unreadable.
        Assert.True(
            runs.Count(r => r == "Date") >= 6,
            $"the trend table's header was drawn {runs.Count(r => r == "Date")} times across {PdfInspector.DeclaredPageCount(pdf)} pages");
    }

    [Fact]
    public void DegradesTextTheBase14FontsCannotSpellRatherThanCorruptingTheFile()
    {
        var report = AnalyticsReports.Populated();
        var cyrillic = report with { Meta = report.Meta with { OrganizerName = "Бошко" } };

        var pdf = Pdf(cyrillic);

        // WinAnsiEncoding has no Cyrillic. The document must still be valid, with
        // the name replaced rather than the byte stream broken - and the CSV
        // export is the one that carries such text intact.
        Assert.True(PdfInspector.IsStructurallyValid(pdf, out var reason), reason);
        Assert.Contains("?????", PdfInspector.AllText(pdf), StringComparison.Ordinal);
    }

    [Fact]
    public void EscapesParenthesesInUserSuppliedTextSoTheContentStreamStaysWellFormed()
    {
        var report = AnalyticsReports.Populated(pages:
        [
            new BookingPagePerformanceDto(
                Guid.NewGuid(), @"Intro (30) \ call", "intro", true, 10, 4, 1, 3, 0.4, 0.2),
        ]);

        var pdf = Pdf(report);

        // An unescaped ")" would end the literal early and corrupt every operator
        // after it - the document would still be bytes, just not a PDF.
        Assert.True(PdfInspector.IsStructurallyValid(pdf, out var reason), reason);
        Assert.Contains(@"Intro (30) \ call", PdfInspector.TextRuns(pdf));
    }
}
