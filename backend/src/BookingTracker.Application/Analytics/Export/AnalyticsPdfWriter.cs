using System.Globalization;
using BookingTracker.Application.Analytics.Dtos;
using BookingTracker.Application.Analytics.Export.Pdf;

namespace BookingTracker.Application.Analytics.Export;

/// <summary>
/// Lays an <see cref="AnalyticsReportDto"/> out as a printable A4 report.
///
/// Structure, in order: a title block with the organizer and the applied filter;
/// the summary as a grid of stat cards; then every section
/// <see cref="AnalyticsReportTables"/> produces, as a table, with a chart drawn
/// above the five sections a chart actually helps. A running header and a
/// "Page N of M" footer sit on every page.
///
/// Two rules the layout keeps:
/// - **Nothing is recomputed.** Tables come from the shared report model, and the
///   charts read the same DTOs the dashboard renders. The only arithmetic here is
///   geometry.
/// - **Every chart labels its values.** A report may be printed in greyscale or
///   read by someone with a colour-vision deficiency, so colour is never the only
///   thing carrying a number - the same rule the dashboard's palette works under.
/// </summary>
public static class AnalyticsPdfWriter
{
    public const string ContentType = "application/pdf";

    private const double Margin = 46;
    private const double HeaderBaseline = 30;
    private const double HeaderRule = 38;
    private const double ContentTop = 62;
    private const double FooterRule = 46;   // measured up from the bottom edge
    private const double FooterBaseline = 30;
    private const double BottomLimit = 58;  // content must stop this far above the bottom edge

    public static byte[] Write(AnalyticsReportDto report) => new Layout(report).Render();

    /// <summary>
    /// Holds the page cursor while the document is emitted. A class rather than a
    /// pile of ref parameters, because almost every draw call needs to know where
    /// the last one finished and whether it still fits on this page.
    /// </summary>
    private sealed class Layout
    {
        // Shared by the drawing methods and by ChartHeight, which has to predict
        // what they will consume before they run.
        private const int CardsPerRow = 5;
        private const int SummaryCardCount = 10;
        private const double CardGap = 8;
        private const double CardHeight = 48;
        private const double TrendChartHeight = 150;
        private const double BarRowHeight = 14;

        private static double StatCardsHeight =>
            (Math.Ceiling(SummaryCardCount / (double)CardsPerRow) * (CardHeight + CardGap)) + 8;

        private static double BarChartHeight(int rows) => rows == 0 ? 0 : (rows * BarRowHeight) + 10;

        private readonly AnalyticsReportDto _report;
        private readonly PdfDocumentBuilder _pdf;
        private readonly double _left;
        private readonly double _right;
        private readonly double _width;
        private readonly double _bottomLimit;
        private double _y;

        public Layout(AnalyticsReportDto report)
        {
            _report = report;
            _pdf = new PdfDocumentBuilder($"Analytics report - {report.Meta.OrganizerName}");
            _left = Margin;
            _right = _pdf.PageWidth - Margin;
            _width = _right - _left;
            _bottomLimit = _pdf.PageHeight - BottomLimit;
        }

        public byte[] Render()
        {
            StartPage();
            TitleBlock();
            FilterPanel();

            foreach (var table in AnalyticsReportTables.Build(_report))
            {
                Section(table);
            }

            StampFooters();
            return _pdf.Build();
        }

        // ---- page frame -------------------------------------------------------

        private void StartPage()
        {
            _pdf.AddPage();
            RunningHeader();
            _y = ContentTop;
        }

        private void RunningHeader()
        {
            _pdf.Text(_left, HeaderBaseline, "BookingTracker  |  Analytics report", PdfFontFace.Bold, 8, AnalyticsReportTheme.InkSecondary);
            _pdf.TextRight(_right, HeaderBaseline, _report.Meta.DateRangeLabel, PdfFontFace.Regular, 8, AnalyticsReportTheme.InkMuted);
            _pdf.Line(_left, HeaderRule, _right, HeaderRule, AnalyticsReportTheme.Grid, 0.6);
        }

        /// <summary>
        /// Footers are stamped only once every page exists, because "of M" cannot
        /// be known before then - which is exactly what PdfDocumentBuilder's
        /// SelectPage is for.
        /// </summary>
        private void StampFooters()
        {
            var total = _pdf.PageCount;
            var ruleY = _pdf.PageHeight - FooterRule;
            var textY = _pdf.PageHeight - FooterBaseline;
            var generated = $"Generated {ReportFormat.ReadableTimestamp(_report.Meta.GeneratedAtUtc)}";

            for (var i = 0; i < total; i++)
            {
                _pdf.SelectPage(i);
                _pdf.Line(_left, ruleY, _right, ruleY, AnalyticsReportTheme.Grid, 0.6);
                _pdf.Text(_left, textY, generated, PdfFontFace.Regular, 7.5, AnalyticsReportTheme.InkMuted);
                _pdf.TextRight(
                    _right, textY,
                    string.Create(CultureInfo.InvariantCulture, $"Page {i + 1} of {total}"),
                    PdfFontFace.Regular, 7.5, AnalyticsReportTheme.InkMuted);
            }
        }

        /// <summary>
        /// Moves to a new page when <paramref name="needed"/> points would not fit
        /// below the cursor. A block taller than a whole page never triggers an
        /// endless run of blank pages: at the top of a fresh page the request is
        /// granted regardless, and the block simply overflows onto the next one.
        /// </summary>
        private void EnsureSpace(double needed)
        {
            if (_y > ContentTop && _y + needed > _bottomLimit) StartPage();
        }

        // ---- opening blocks ---------------------------------------------------

        private void TitleBlock()
        {
            _y += 6;
            _pdf.Text(_left, _y + 18, "Analytics report", PdfFontFace.Bold, 22, AnalyticsReportTheme.InkPrimary);
            _y += 26;
            _pdf.Text(_left, _y + 10, _report.Meta.OrganizerName, PdfFontFace.Regular, 10.5, AnalyticsReportTheme.InkSecondary);
            if (!string.IsNullOrWhiteSpace(_report.Meta.OrganizerEmail))
            {
                _pdf.TextRight(_right, _y + 10, _report.Meta.OrganizerEmail, PdfFontFace.Regular, 9, AnalyticsReportTheme.InkMuted);
            }
            _y += 22;
        }

        /// <summary>
        /// The applied filter, printed rather than implied. A page of figures with
        /// no statement of the window it covers is not a report anyone can act on
        /// a month later.
        /// </summary>
        private void FilterPanel()
        {
            (string Label, string Value)[] fields =
            [
                ("Date range", _report.Meta.DateRangeLabel),
                ("Booking page", _report.Meta.BookingPageLabel),
                ("Status", _report.Meta.StatusLabel),
                ("Time zone", _report.Meta.TimeZoneId),
                ("Generated", ReportFormat.ReadableTimestamp(_report.Meta.GeneratedAtUtc)),
            ];

            // Three columns over two rows rather than five across: a full date
            // range ("2026-07-06 to 2026-08-04") does not fit in a fifth of the
            // page, and a filter statement that is itself truncated defeats the
            // point of printing it.
            const int Columns = 3;
            const double RowHeight = 34;
            var rows = (int)Math.Ceiling(fields.Length / (double)Columns);
            var panelHeight = (rows * RowHeight) + 10;

            EnsureSpace(panelHeight + 12);
            _pdf.Rect(_left, _y, _width, panelHeight, AnalyticsReportTheme.PanelFill, AnalyticsReportTheme.Grid, 0.6);

            var columnWidth = (_width - 24) / Columns;
            for (var i = 0; i < fields.Length; i++)
            {
                var x = _left + 12 + ((i % Columns) * columnWidth);
                var top = _y + 5 + ((i / Columns) * RowHeight);
                var available = columnWidth - 10;

                _pdf.Text(x, top + 12, fields[i].Label.ToUpperInvariant(), PdfFontFace.Bold, 6.5, AnalyticsReportTheme.InkMuted);
                _pdf.Text(
                    x, top + 26,
                    PdfFontMetrics.Truncate(fields[i].Value, PdfFontFace.Regular, 9, available),
                    PdfFontFace.Regular, 9, AnalyticsReportTheme.InkPrimary);
            }

            _y += panelHeight + 18;
        }

        // ---- sections ---------------------------------------------------------

        private void Section(ReportTable table)
        {
            // The heading, any chart, and at least the table's header row are kept
            // together: a heading stranded at the foot of a page is the classic
            // generated-report tell. The chart's own height has to be part of that
            // reservation, or a tall chart pushes itself over the break and leaves
            // the heading behind.
            Heading(table.Title, keepWithNext: ChartHeight(table.Key) + 40);
            Chart(table.Key);
            Table(table);
            _y += 16;
        }

        /// <summary>What <see cref="Chart"/> is about to consume, so the heading can reserve it.</summary>
        private double ChartHeight(ReportTableKey key) => key switch
        {
            ReportTableKey.Summary => StatCardsHeight,
            ReportTableKey.Trend => _report.Bookings.Trend.Count == 0 ? 0 : TrendChartHeight + 6,
            ReportTableKey.StatusDistribution => BarChartHeight(_report.Bookings.StatusDistribution.Count),
            ReportTableKey.Funnel => BarChartHeight(_report.Funnel.Steps.Count),
            ReportTableKey.Weekdays => BarChartHeight(_report.Bookings.BookingsByWeekday.Count),
            ReportTableKey.Hours => BarChartHeight(_report.Bookings.BookingsByHour.Count),
            _ => 0,
        };

        private void Heading(string text, double keepWithNext)
        {
            EnsureSpace(22 + keepWithNext);
            _pdf.Text(_left, _y + 10, text, PdfFontFace.Bold, 12, AnalyticsReportTheme.InkPrimary);
            _y += 15;
            _pdf.Line(_left, _y, _right, _y, AnalyticsReportTheme.Axis, 0.8);
            _y += 12;
        }

        /// <summary>
        /// Charts are drawn for the five sections where a shape says something a
        /// column of numbers does not; the numbers still follow underneath in every
        /// case. Values come from the analytics DTOs, never from the formatted
        /// table cells.
        /// </summary>
        private void Chart(ReportTableKey key)
        {
            switch (key)
            {
                case ReportTableKey.Summary:
                    StatCards();
                    break;
                case ReportTableKey.Trend:
                    TrendChart();
                    break;
                case ReportTableKey.StatusDistribution:
                    BarChart(_report.Bookings.StatusDistribution
                        .Select(s => (s.Status, (double)s.Count, (string?)AnalyticsReportTheme.StatusColor(s.Status)))
                        .ToList());
                    break;
                case ReportTableKey.Funnel:
                    FunnelChart();
                    break;
                case ReportTableKey.Weekdays:
                    BarChart(_report.Bookings.BookingsByWeekday
                        .Select(d => (d.Label, (double)d.Count, (string?)null))
                        .ToList());
                    break;
                case ReportTableKey.Hours:
                    BarChart(_report.Bookings.BookingsByHour
                        .Select(h => (h.Hour.ToString("00", CultureInfo.InvariantCulture) + ":00", (double)h.Count, (string?)null))
                        .ToList());
                    break;
                default:
                    break;
            }
        }

        private void StatCards()
        {
            var s = _report.Bookings.Summary;
            var completion = _report.Bookings.CompletionTime;

            (string Label, string Value, string Hint)[] cards =
            [
                ("Visitors", ReportFormat.Number(s.TotalSessions), "Sessions started"),
                ("Bookings", ReportFormat.Number(s.Confirmed), "Confirmed"),
                ("Completion rate", ReportFormat.Percent(s.CompletionRate, 0) + "%", "Visitors who booked"),
                ("Upcoming", ReportFormat.Number(s.Upcoming), "Still to happen"),
                ("Completed", ReportFormat.Number(s.Completed), "Already met"),
                ("Cancelled", ReportFormat.Number(s.Cancelled), string.Empty),
                ("Rescheduled", ReportFormat.Number(s.Rescheduled), "Moved at least once"),
                ("Abandoned", ReportFormat.Number(s.Abandoned), "Left without booking"),
                ("Booking pages", ReportFormat.Number(s.TotalBookingPages), $"{s.ActiveBookingPages} active"),
                ("Avg. time to book", ReportFormat.Duration(completion.AverageSeconds),
                    completion.SampleSize > 0 ? $"{completion.SampleSize} bookings" : "No data"),
            ];

            var cardWidth = (_width - (CardGap * (CardsPerRow - 1))) / CardsPerRow;
            var rows = (int)Math.Ceiling(cards.Length / (double)CardsPerRow);

            EnsureSpace(StatCardsHeight);

            for (var i = 0; i < cards.Length; i++)
            {
                var column = i % CardsPerRow;
                var row = i / CardsPerRow;
                var x = _left + (column * (cardWidth + CardGap));
                var top = _y + (row * (CardHeight + CardGap));

                _pdf.Rect(x, top, cardWidth, CardHeight, AnalyticsReportTheme.Surface, AnalyticsReportTheme.Grid, 0.6);
                _pdf.Text(x + 8, top + 14, cards[i].Label.ToUpperInvariant(), PdfFontFace.Bold, 6.5, AnalyticsReportTheme.InkMuted);
                _pdf.Text(
                    x + 8, top + 32,
                    PdfFontMetrics.Truncate(cards[i].Value, PdfFontFace.Bold, 15, cardWidth - 16),
                    PdfFontFace.Bold, 15, AnalyticsReportTheme.InkPrimary);
                _pdf.Text(
                    x + 8, top + 42,
                    PdfFontMetrics.Truncate(cards[i].Hint, PdfFontFace.Regular, 6.5, cardWidth - 16),
                    PdfFontFace.Regular, 6.5, AnalyticsReportTheme.InkMuted);
            }

            _y += (rows * (CardHeight + CardGap)) + 8;
        }

        /// <summary>Two series (sessions started, bookings confirmed) on a shared axis, with a legend and value-bearing axis labels.</summary>
        private void TrendChart()
        {
            var points = _report.Bookings.Trend;
            if (points.Count == 0) return;

            const double AxisGutter = 30;   // room for the y labels
            const double LegendHeight = 16;
            const double LabelGutter = 16;  // room for the x labels

            EnsureSpace(TrendChartHeight + 6);

            var top = _y;
            var plotTop = top + LegendHeight;
            var plotBottom = top + TrendChartHeight - LabelGutter;
            var plotLeft = _left + AxisGutter;
            var plotRight = _right;
            var plotHeight = plotBottom - plotTop;

            Legend(top, [("Sessions", AnalyticsReportTheme.Series[0]), ("Bookings", AnalyticsReportTheme.Series[1])]);

            // Four gridlines, each labelled - a gridline with no number on it is
            // decoration, and this chart has to be readable on paper.
            const int GridLines = 4;
            var max = AxisMax(points.Max(p => Math.Max(p.Sessions, p.Bookings)), GridLines);

            for (var i = 0; i <= GridLines; i++)
            {
                var value = max * (GridLines - i) / GridLines;
                var y = plotTop + (plotHeight * i / GridLines);
                _pdf.Line(plotLeft, y, plotRight, y, i == GridLines ? AnalyticsReportTheme.Axis : AnalyticsReportTheme.Grid, 0.5);
                _pdf.TextRight(
                    plotLeft - 4, y + 2.5,
                    value.ToString(CultureInfo.InvariantCulture),
                    PdfFontFace.Regular, 6.5, AnalyticsReportTheme.InkMuted);
            }

            double X(int index) => points.Count == 1
                ? (plotLeft + plotRight) / 2
                : plotLeft + ((plotRight - plotLeft) * index / (points.Count - 1));
            double Y(int value) => plotBottom - (max == 0 ? 0 : plotHeight * value / max);

            var sessions = points.Select((p, i) => (X(i), Y(p.Sessions))).ToList();
            var bookings = points.Select((p, i) => (X(i), Y(p.Bookings))).ToList();

            if (points.Count == 1)
            {
                // A single day has no line to draw; a marker still shows where it sits.
                _pdf.Rect(sessions[0].Item1 - 1.5, sessions[0].Item2 - 1.5, 3, 3, AnalyticsReportTheme.Series[0]);
                _pdf.Rect(bookings[0].Item1 - 1.5, bookings[0].Item2 - 1.5, 3, 3, AnalyticsReportTheme.Series[1]);
            }
            else
            {
                _pdf.Polyline(sessions, AnalyticsReportTheme.Series[0], 1.4);
                _pdf.Polyline(bookings, AnalyticsReportTheme.Series[1], 1.4);
            }

            // First, middle and last dates only: one label per day would collide on
            // anything longer than a fortnight.
            var labelY = plotBottom + 11;
            _pdf.Text(plotLeft, labelY, ReportFormat.Date(points[0].Date), PdfFontFace.Regular, 6.5, AnalyticsReportTheme.InkMuted);
            if (points.Count > 2)
            {
                _pdf.TextCenter(
                    (plotLeft + plotRight) / 2, labelY,
                    ReportFormat.Date(points[points.Count / 2].Date), PdfFontFace.Regular, 6.5, AnalyticsReportTheme.InkMuted);
            }
            if (points.Count > 1)
            {
                _pdf.TextRight(plotRight, labelY, ReportFormat.Date(points[^1].Date), PdfFontFace.Regular, 6.5, AnalyticsReportTheme.InkMuted);
            }

            _y = top + TrendChartHeight + 6;
        }

        private void FunnelChart()
        {
            var steps = _report.Funnel.Steps;
            if (steps.Count == 0) return;

            var max = steps.Max(s => s.Count);
            BarChart(steps
                .Select(s => (
                    s.Step,
                    (double)s.Count,
                    (string?)AnalyticsReportTheme.SequentialStep(s.Count, max)))
                .ToList());
        }

        /// <summary>
        /// Horizontal bars with the value printed at the end of each. Chosen over a
        /// pie for the status split specifically: a bar's length is comparable at a
        /// glance in print, where a pie's angles are not, and every value is legible
        /// without the colour key.
        /// </summary>
        private void BarChart(IReadOnlyList<(string Label, double Value, string? Color)> bars)
        {
            if (bars.Count == 0) return;

            const double LabelWidth = 96;
            const double ValueWidth = 34;

            EnsureSpace(BarChartHeight(bars.Count));

            var max = bars.Max(b => b.Value);
            var trackLeft = _left + LabelWidth;
            var trackWidth = _right - trackLeft - ValueWidth;

            for (var i = 0; i < bars.Count; i++)
            {
                var (label, value, color) = bars[i];
                var top = _y + (i * BarRowHeight);

                _pdf.Text(
                    _left, top + 9.5,
                    PdfFontMetrics.Truncate(label, PdfFontFace.Regular, 8, LabelWidth - 6),
                    PdfFontFace.Regular, 8, AnalyticsReportTheme.InkSecondary);

                // The track is drawn even at zero, so an empty category still reads
                // as "measured, none" rather than as a missing row.
                _pdf.Rect(trackLeft, top + 3, trackWidth, 8, AnalyticsReportTheme.PanelFill);

                if (max > 0 && value > 0)
                {
                    _pdf.Rect(
                        trackLeft, top + 3, Math.Max(1.5, trackWidth * value / max), 8,
                        color ?? AnalyticsReportTheme.SequentialStep(value, max));
                }

                _pdf.TextRight(
                    _right, top + 9.5,
                    ((int)Math.Round(value)).ToString(CultureInfo.InvariantCulture),
                    PdfFontFace.Regular, 8, AnalyticsReportTheme.InkPrimary);
            }

            _y += BarChartHeight(bars.Count);
        }

        private void Legend(double top, IReadOnlyList<(string Label, string Color)> entries)
        {
            var x = _left + 30;
            foreach (var (label, color) in entries)
            {
                _pdf.Rect(x, top + 3, 7, 7, color);
                _pdf.Text(x + 11, top + 9, label, PdfFontFace.Regular, 7.5, AnalyticsReportTheme.InkSecondary);
                x += 11 + PdfFontMetrics.Width(label, PdfFontFace.Regular, 7.5) + 16;
            }
        }

        // ---- tables -----------------------------------------------------------

        private void Table(ReportTable table)
        {
            const double HeaderHeight = 17;
            const double RowHeight = 15;
            const double FontSize = 8;
            const double Padding = 6;

            var totalWeight = table.Columns.Sum(c => c.Weight);
            var widths = table.Columns.Select(c => _width * c.Weight / totalWeight).ToArray();

            if (table.IsEmpty)
            {
                EnsureSpace(RowHeight + 4);
                _pdf.Text(_left, _y + 10, "No data in this range.", PdfFontFace.Regular, FontSize, AnalyticsReportTheme.InkMuted);
                _y += RowHeight;
                return;
            }

            void DrawHeader()
            {
                _pdf.Rect(_left, _y, _width, HeaderHeight, AnalyticsReportTheme.PanelFill);
                DrawCells(table.Columns.Select(c => c.Header).ToList(), widths, table.Columns, _y, HeaderHeight, PdfFontFace.Bold, AnalyticsReportTheme.InkSecondary, FontSize, Padding);
                _y += HeaderHeight;
            }

            EnsureSpace(HeaderHeight + RowHeight);
            DrawHeader();

            for (var i = 0; i < table.Rows.Count; i++)
            {
                if (_y + RowHeight > _bottomLimit)
                {
                    // A table that spills onto the next page repeats its header
                    // there; unlabelled columns halfway through a report are
                    // unreadable.
                    StartPage();
                    DrawHeader();
                }

                if (i % 2 == 1) _pdf.Rect(_left, _y, _width, RowHeight, AnalyticsReportTheme.ZebraFill);
                DrawCells(table.Rows[i], widths, table.Columns, _y, RowHeight, PdfFontFace.Regular, AnalyticsReportTheme.InkPrimary, FontSize, Padding);
                _y += RowHeight;
            }

            _pdf.Line(_left, _y, _right, _y, AnalyticsReportTheme.Grid, 0.6);
            _y += 2;
        }

        private void DrawCells(
            IReadOnlyList<string> cells, double[] widths, IReadOnlyList<ReportColumn> columns,
            double top, double height, PdfFontFace face, string color, double fontSize, double padding)
        {
            var baseline = top + (height / 2) + (fontSize * PdfFontMetrics.Ascent / 2) - 1;
            var x = _left;

            for (var c = 0; c < widths.Length; c++)
            {
                var text = c < cells.Count ? cells[c] : string.Empty;
                var available = widths[c] - (padding * 2);
                var clipped = PdfFontMetrics.Truncate(text, face, fontSize, available);

                if (columns[c].Align == ReportAlign.Right)
                {
                    _pdf.TextRight(x + widths[c] - padding, baseline, clipped, face, fontSize, color);
                }
                else
                {
                    _pdf.Text(x + padding, baseline, clipped, face, fontSize, color);
                }

                x += widths[c];
            }
        }

        /// <summary>
        /// An axis maximum that divides into <paramref name="gridLines"/> whole
        /// steps, so every gridline carries a round integer.
        ///
        /// Rounding the maximum alone is not enough: 6 rounds up to a tidy 10, but
        /// 10 over four gridlines puts a label at 2.5, which then prints as "2"
        /// sitting where 2.5 actually is - a chart that misreports its own scale.
        /// Choosing the *step* first and multiplying back up avoids that entirely.
        /// </summary>
        private static int AxisMax(int peak, int gridLines)
        {
            if (peak <= 0) return gridLines;

            var rawStep = peak / (double)gridLines;
            var magnitude = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(rawStep, 1))));

            foreach (var step in new[] { 1.0, 2.0, 2.5, 3.0, 4.0, 5.0, 6.0, 8.0, 10.0 })
            {
                var candidate = step * magnitude;
                if (candidate >= rawStep && Math.Abs(candidate - Math.Round(candidate)) < 1e-9)
                {
                    return (int)Math.Round(candidate) * gridLines;
                }
            }

            return (int)(10 * magnitude) * gridLines;
        }
    }
}
