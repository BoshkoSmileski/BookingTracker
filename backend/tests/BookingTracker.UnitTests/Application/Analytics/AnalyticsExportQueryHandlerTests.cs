using System.Text;
using BookingTracker.Application.Analytics;
using BookingTracker.Application.Analytics.Dtos;
using BookingTracker.Application.Analytics.Export;
using BookingTracker.Application.Analytics.Queries.ExportAnalyticsCsv;
using BookingTracker.Application.Analytics.Queries.ExportAnalyticsPdf;
using BookingTracker.Application.Analytics.Queries.GetActivityFeed;
using BookingTracker.Application.Analytics.Queries.GetAnalyticsReport;
using BookingTracker.Application.Analytics.Queries.GetBookingAnalytics;
using BookingTracker.Application.Analytics.Queries.GetConversionFunnel;
using BookingTracker.Application.Analytics.Queries.GetOperationalAnalytics;
using BookingTracker.Application.Common.Exceptions;
using BookingTracker.Domain.Enums;
using BookingTracker.UnitTests.TestSupport;

namespace BookingTracker.UnitTests.Application.Analytics;

/// <summary>
/// The export end of the feature: that a download really is the dashboard.
///
/// These run through a real MediatR pipeline over a real InMemory DbContext
/// (see <see cref="AnalyticsExportHost"/>), because the whole claim being made
/// is that the export dispatches the same four analytics queries the dashboard
/// calls. Stubbing that dispatch would leave nothing worth asserting.
///
/// Dates are derived from DateTime.UtcNow rather than written down: the
/// upcoming/completed split and the trend's gap-filling both read the clock
///.
/// </summary>
public class AnalyticsExportQueryHandlerTests
{
    private static string CsvText(AnalyticsExportFileDto file) =>
        Encoding.UTF8.GetString(file.Content, Encoding.UTF8.GetPreamble().Length, file.Content.Length - Encoding.UTF8.GetPreamble().Length);

    // ---- the report itself ------------------------------------------------

    [Fact]
    public async Task Report_IsAssembledFromTheSameQueriesTheDashboardCalls()
    {
        await using var host = AnalyticsExportHost.Create();
        var seeded = await AnalyticsDataset.SeedAsync(host.Db);
        var filter = new AnalyticsFilter(seeded.Organizer.Id);

        var report = await host.Sender.Send(new GetAnalyticsReportQuery(filter));

        var bookings = await host.Sender.Send(new GetBookingAnalyticsQuery(filter));
        var funnel = await host.Sender.Send(new GetConversionFunnelQuery(filter));
        var operations = await host.Sender.Send(new GetOperationalAnalyticsQuery(filter));
        var activity = await host.Sender.Send(new GetActivityFeedQuery(filter, 100));

        // REGRESSION: the export must never recompute a metric. Every panel is
        // compared against the very query the dashboard issues for it.
        //
        // Compared member by member rather than with one record equality check:
        // these DTOs hold IReadOnlyList members, and a record compares those by
        // reference, so two separately-computed-but-identical results would never
        // be Equal. Assert.Equal over a sequence does compare element by element,
        // and the elements themselves are records of scalars.
        Assert.Equal(bookings.Summary, report.Bookings.Summary);
        Assert.Equal(bookings.CompletionTime, report.Bookings.CompletionTime);
        Assert.Equal(bookings.TimeZoneId, report.Bookings.TimeZoneId);
        Assert.Equal(bookings.Trend, report.Bookings.Trend);
        Assert.Equal(bookings.StatusDistribution, report.Bookings.StatusDistribution);
        Assert.Equal(bookings.BookingsByWeekday, report.Bookings.BookingsByWeekday);
        Assert.Equal(bookings.BookingsByHour, report.Bookings.BookingsByHour);
        Assert.Equal(bookings.PagePerformance, report.Bookings.PagePerformance);

        Assert.Equal(funnel.Steps, report.Funnel.Steps);
        Assert.Equal(funnel.ConversionRate, report.Funnel.ConversionRate);
        Assert.Equal(funnel.Abandonment.TotalAbandoned, report.Funnel.Abandonment.TotalAbandoned);
        Assert.Equal(funnel.Abandonment.AbandonmentRate, report.Funnel.Abandonment.AbandonmentRate);
        Assert.Equal(funnel.Abandonment.ByStep, report.Funnel.Abandonment.ByStep);

        Assert.Equal(operations.Reminders, report.Operations.Reminders);
        Assert.Equal(operations.Calendar, report.Operations.Calendar);
        Assert.Equal(operations.Email.ByType, report.Operations.Email.ByType);
        Assert.Equal(operations.Email.DeliveryRate, report.Operations.Email.DeliveryRate);

        Assert.Equal(activity, report.Activity);
    }

    [Fact]
    public async Task Report_DescribesWhoAndWhenAndUnderWhichFilter()
    {
        await using var host = AnalyticsExportHost.Create();
        var seeded = await AnalyticsDataset.SeedAsync(host.Db);
        var before = DateTime.UtcNow;

        var report = await host.Sender.Send(new GetAnalyticsReportQuery(new AnalyticsFilter(
            seeded.Organizer.Id,
            From: new DateOnly(2026, 1, 1),
            To: new DateOnly(2026, 12, 31),
            BookingPageId: seeded.PageB.Id,
            Status: BookingSessionStatus.Submitted)));

        Assert.Equal("Test Organizer", report.Meta.OrganizerName);
        Assert.Equal("organizer@example.com", report.Meta.OrganizerEmail);
        Assert.Equal("2026-01-01 to 2026-12-31", report.Meta.DateRangeLabel);
        // The page id becomes its title, resolved once so both formats say the same thing.
        Assert.Equal("Test Meeting", report.Meta.BookingPageLabel);
        Assert.Equal("Submitted", report.Meta.StatusLabel);
        Assert.InRange(report.Meta.GeneratedAtUtc, before, DateTime.UtcNow);
    }

    [Theory]
    [InlineData(null, null, "All time")]
    [InlineData("2026-01-01", null, "2026-01-01 onwards")]
    [InlineData(null, "2026-12-31", "Up to 2026-12-31")]
    [InlineData("2026-01-01", "2026-12-31", "2026-01-01 to 2026-12-31")]
    public async Task Report_WordsEveryCombinationOfAnOpenEndedRange(string? from, string? to, string expected)
    {
        await using var host = AnalyticsExportHost.Create();
        var seeded = await AnalyticsDataset.SeedAsync(host.Db);

        var report = await host.Sender.Send(new GetAnalyticsReportQuery(new AnalyticsFilter(
            seeded.Organizer.Id,
            From: from is null ? null : DateOnly.Parse(from),
            To: to is null ? null : DateOnly.Parse(to))));

        Assert.Equal(expected, report.Meta.DateRangeLabel);
    }

    [Fact]
    public async Task Report_DefaultsToAnUnfilteredLabelWhenNothingWasNarrowed()
    {
        await using var host = AnalyticsExportHost.Create();
        var seeded = await AnalyticsDataset.SeedAsync(host.Db);

        var report = await host.Sender.Send(new GetAnalyticsReportQuery(new AnalyticsFilter(seeded.Organizer.Id)));

        Assert.Equal("All booking pages", report.Meta.BookingPageLabel);
        Assert.Equal("All statuses", report.Meta.StatusLabel);
    }

    // ---- authorization ----------------------------------------------------

    [Theory]
    [InlineData("csv")]
    [InlineData("pdf")]
    public async Task Export_ForAnotherOrganizersPage_ThrowsNotFound(string format)
    {
        await using var host = AnalyticsExportHost.Create();
        var seeded = await AnalyticsDataset.SeedAsync(host.Db);
        var stranger = TestEntities.CreateOrganizer(email: "stranger@example.com");
        host.Db.Organizers.Add(stranger);
        await host.Db.SaveChangesAsync();

        var filter = new AnalyticsFilter(stranger.Id, BookingPageId: seeded.PageA.Id);

        // Ownership is enforced in AnalyticsScope, which the export reaches through
        // the same queries - so the export inherits it rather than re-checking it.
        await Assert.ThrowsAsync<NotFoundException>(() => format == "csv"
            ? host.Sender.Send(new ExportAnalyticsCsvQuery(filter))
            : host.Sender.Send(new ExportAnalyticsPdfQuery(filter)));
    }

    [Fact]
    public async Task Export_ForAnOrganizerWithNoData_SucceedsWithAnEmptyReport()
    {
        await using var host = AnalyticsExportHost.Create();
        var stranger = TestEntities.CreateOrganizer(email: "nobody@example.com");
        host.Db.Organizers.Add(stranger);
        await host.Db.SaveChangesAsync();

        var file = await host.Sender.Send(new ExportAnalyticsCsvQuery(new AnalyticsFilter(stranger.Id)));

        // Another organizer's numbers must never leak in through an unscoped query.
        Assert.Contains("Visitors (sessions started),0", CsvText(file), StringComparison.Ordinal);
        Assert.DoesNotContain("Test Meeting", CsvText(file), StringComparison.Ordinal);
    }

    // ---- CSV --------------------------------------------------------------

    [Fact]
    public async Task Csv_CarriesTheSameFiguresTheDashboardQueriesReturn()
    {
        await using var host = AnalyticsExportHost.Create();
        var seeded = await AnalyticsDataset.SeedAsync(host.Db);
        var filter = new AnalyticsFilter(seeded.Organizer.Id);

        var dashboard = await host.Sender.Send(new GetBookingAnalyticsQuery(filter));
        var funnel = await host.Sender.Send(new GetConversionFunnelQuery(filter));
        var csv = CsvText(await host.Sender.Send(new ExportAnalyticsCsvQuery(filter)));

        // REGRESSION: exported values must match the dashboard exactly. The seeded
        // set is 6 sessions, 3 confirmed, 1 cancelled, 1 abandoned, 1 active.
        Assert.Equal(6, dashboard.Summary.TotalSessions);
        Assert.Contains($"Visitors (sessions started),{dashboard.Summary.TotalSessions}", csv, StringComparison.Ordinal);
        Assert.Contains($"Bookings confirmed,{dashboard.Summary.Confirmed}", csv, StringComparison.Ordinal);
        Assert.Contains($"Cancelled,{dashboard.Summary.Cancelled}", csv, StringComparison.Ordinal);
        Assert.Contains($"Abandoned,{dashboard.Summary.Abandoned}", csv, StringComparison.Ordinal);
        Assert.Contains($"Completion rate (%),{ReportFormat.Percent(dashboard.Summary.CompletionRate)}", csv, StringComparison.Ordinal);
        Assert.Contains($"Booking confirmed,{funnel.Steps.Single(s => s.Step == "Booking confirmed").Count}", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Csv_NarrowsToOneBookingPageExactlyAsTheDashboardDoes()
    {
        await using var host = AnalyticsExportHost.Create();
        var seeded = await AnalyticsDataset.SeedAsync(host.Db);
        var filter = new AnalyticsFilter(seeded.Organizer.Id, BookingPageId: seeded.PageB.Id);

        var dashboard = await host.Sender.Send(new GetBookingAnalyticsQuery(filter));
        var csv = CsvText(await host.Sender.Send(new ExportAnalyticsCsvQuery(filter)));

        Assert.Equal(1, dashboard.Summary.TotalSessions);
        Assert.Contains("Visitors (sessions started),1", csv, StringComparison.Ordinal);
        Assert.Contains("page-b", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("page-a", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Csv_NarrowsToOneStatus()
    {
        await using var host = AnalyticsExportHost.Create();
        var seeded = await AnalyticsDataset.SeedAsync(host.Db);
        var filter = new AnalyticsFilter(seeded.Organizer.Id, Status: BookingSessionStatus.Abandoned);

        var csv = CsvText(await host.Sender.Send(new ExportAnalyticsCsvQuery(filter)));

        Assert.Contains("Status,Abandoned", csv, StringComparison.Ordinal);
        Assert.Contains("Visitors (sessions started),1", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Csv_ExcludesEverythingOutsideTheDateRange()
    {
        await using var host = AnalyticsExportHost.Create();
        var seeded = await AnalyticsDataset.SeedAsync(host.Db);

        // Everything was seeded "now", so a window entirely in the past is empty -
        // but the trend must still span the requested window, gap-filled.
        var csv = CsvText(await host.Sender.Send(new ExportAnalyticsCsvQuery(new AnalyticsFilter(
            seeded.Organizer.Id,
            From: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-60)),
            To: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30))))));

        Assert.Contains("Visitors (sessions started),0", csv, StringComparison.Ordinal);
        var trendRows = csv.Split("\r\n").Count(l => l.EndsWith(",0,0", StringComparison.Ordinal));
        Assert.Equal(31, trendRows);
    }

    [Fact]
    public async Task Csv_IsNamedAfterTheFilterThatProducedIt()
    {
        await using var host = AnalyticsExportHost.Create();
        var seeded = await AnalyticsDataset.SeedAsync(host.Db);

        var all = await host.Sender.Send(new ExportAnalyticsCsvQuery(new AnalyticsFilter(
            seeded.Organizer.Id, From: new DateOnly(2026, 7, 6), To: new DateOnly(2026, 8, 4))));
        var onePage = await host.Sender.Send(new ExportAnalyticsCsvQuery(new AnalyticsFilter(
            seeded.Organizer.Id, From: new DateOnly(2026, 7, 6), To: new DateOnly(2026, 8, 4), BookingPageId: seeded.PageA.Id)));
        var allTime = await host.Sender.Send(new ExportAnalyticsCsvQuery(new AnalyticsFilter(seeded.Organizer.Id)));

        Assert.Equal("bookingtracker-analytics-20260706-to-20260804.csv", all.FileName);
        Assert.Equal("bookingtracker-analytics-test-meeting-20260706-to-20260804.csv", onePage.FileName);
        Assert.Equal("bookingtracker-analytics-all-time.csv", allTime.FileName);
        Assert.Equal("text/csv", all.ContentType);
    }

    // ---- PDF --------------------------------------------------------------

    [Fact]
    public async Task Pdf_IsAValidDocumentCarryingTheSameFiguresAsTheCsv()
    {
        await using var host = AnalyticsExportHost.Create();
        var seeded = await AnalyticsDataset.SeedAsync(host.Db);
        var filter = new AnalyticsFilter(seeded.Organizer.Id);

        var report = await host.Sender.Send(new GetAnalyticsReportQuery(filter));
        var pdf = await host.Sender.Send(new ExportAnalyticsPdfQuery(filter));

        Assert.Equal("application/pdf", pdf.ContentType);
        Assert.Equal("bookingtracker-analytics-all-time.pdf", pdf.FileName);
        Assert.True(PdfInspector.IsStructurallyValid(pdf.Content, out var reason), reason);

        // REGRESSION: the two downloads are rendered from one report model, so
        // every cell in the CSV is also drawn in the PDF.
        var runs = PdfInspector.TextRuns(pdf.Content);
        var summary = AnalyticsReportTables.Build(report).Single(t => t.Key == ReportTableKey.Summary);
        foreach (var cell in summary.Rows.SelectMany(r => r))
        {
            Assert.Contains(cell, runs);
        }
    }

    [Fact]
    public async Task Pdf_PrintsTheAppliedFilterOnTheReport()
    {
        await using var host = AnalyticsExportHost.Create();
        var seeded = await AnalyticsDataset.SeedAsync(host.Db);

        var pdf = await host.Sender.Send(new ExportAnalyticsPdfQuery(new AnalyticsFilter(
            seeded.Organizer.Id, BookingPageId: seeded.PageB.Id, Status: BookingSessionStatus.Submitted)));
        var text = PdfInspector.AllText(pdf.Content);

        Assert.Contains("Test Organizer", text, StringComparison.Ordinal);
        Assert.Contains("Submitted", text, StringComparison.Ordinal);
        Assert.Contains("All time", text, StringComparison.Ordinal);
        Assert.Contains("Page 1 of", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pdf_ForAnOrganizerWithNoData_IsStillAValidReport()
    {
        await using var host = AnalyticsExportHost.Create();
        var stranger = TestEntities.CreateOrganizer(email: "nobody@example.com");
        host.Db.Organizers.Add(stranger);
        await host.Db.SaveChangesAsync();

        var pdf = await host.Sender.Send(new ExportAnalyticsPdfQuery(new AnalyticsFilter(stranger.Id)));

        Assert.True(PdfInspector.IsStructurallyValid(pdf.Content, out var reason), reason);
        Assert.Contains("No data in this range.", PdfInspector.AllText(pdf.Content), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BothFormatsRenderIdenticalCellsForTheSameFilter()
    {
        await using var host = AnalyticsExportHost.Create();
        var seeded = await AnalyticsDataset.SeedAsync(host.Db);
        var filter = new AnalyticsFilter(seeded.Organizer.Id);

        var csv = CsvText(await host.Sender.Send(new ExportAnalyticsCsvQuery(filter)));
        var pdfRuns = PdfInspector.TextRuns((await host.Sender.Send(new ExportAnalyticsPdfQuery(filter))).Content);

        // Both reads happen moments apart against the same data, so every section
        // heading and every metric label must appear in both.
        foreach (var table in AnalyticsReportTables.Build(await host.Sender.Send(new GetAnalyticsReportQuery(filter))))
        {
            Assert.Contains(table.Title, csv, StringComparison.Ordinal);
            Assert.Contains(table.Title, pdfRuns);
        }
    }
}
