using System.Text;
using BookingTracker.Application.Analytics.Export.Pdf;
using BookingTracker.UnitTests.TestSupport;

namespace BookingTracker.UnitTests.Application.Analytics.Export;

/// <summary>
/// The PDF layer under the report: text encoding, font metrics, and the document
/// builder itself. These are the pieces where being wrong produces a file that
/// looks fine as bytes and fails to open, so they are tested directly rather
/// than only through the report that uses them.
/// </summary>
public class PdfTextEncodingTests
{
    [Theory]
    [InlineData("Booking", new byte[] { 0x42, 0x6F, 0x6F, 0x6B, 0x69, 0x6E, 0x67 })]
    [InlineData("é", new byte[] { 0xE9 })]              // Latin-1 supplement, code-for-code
    [InlineData("’", new byte[] { 0x92 })]              // right single quote, the 0x80-0x9F block
    [InlineData("€", new byte[] { 0x80 })]              // euro
    [InlineData("—", new byte[] { 0x97 })]              // em dash
    public void EncodesWhatWindows1252Can(string text, byte[] expected) =>
        Assert.Equal(expected, PdfTextEncoding.ToWinAnsi(text));

    [Fact]
    public void StripsDiacriticsFromLatinLettersTheCodePageLacks()
    {
        // "c with acute" and "a with breve" are not in Windows-1252; falling back
        // to the base letter keeps a name readable, which a question mark would not.
        Assert.Equal("cao", Encoding.ASCII.GetString(PdfTextEncoding.ToWinAnsi("ćaŏ")));
    }

    [Fact]
    public void ReplacesCharactersWithNoLatinBaseAtAll()
    {
        // Cyrillic has no Latin decomposition; five characters in, five out, so
        // measurement and drawing still agree.
        Assert.Equal("?????", Encoding.ASCII.GetString(PdfTextEncoding.ToWinAnsi("Бошко")));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a (b) c", @"a \(b\) c")]
    [InlineData(@"back\slash", @"back\\slash")]
    public void EscapesWhatWouldBreakAPdfLiteral(string text, string expected) =>
        Assert.Equal(expected, PdfTextEncoding.ToPdfLiteral(text));

    [Fact]
    public void WritesHighBytesAsOctalSoTheLiteralStaysAscii()
    {
        var literal = PdfTextEncoding.ToPdfLiteral("café");

        Assert.Equal(@"caf\351", literal);
        Assert.All(literal, c => Assert.InRange(c, ' ', '~'));
    }
}

public class PdfFontMetricsTests
{
    [Fact]
    public void MeasuresAgainstTheRealHelveticaAdvanceWidths()
    {
        // "W" is 944/1000 em in Helvetica and "i" is 222 - at 10pt that is
        // 9.44 + 2.22. A wrong table shows up here before it shows up as a
        // mis-aligned column.
        Assert.Equal(11.66, PdfFontMetrics.Width("Wi", PdfFontFace.Regular, 10), 3);
    }

    [Fact]
    public void BoldIsWiderThanRegularForTheSameText() =>
        Assert.True(
            PdfFontMetrics.Width("Bookings", PdfFontFace.Bold, 10) >
            PdfFontMetrics.Width("Bookings", PdfFontFace.Regular, 10));

    [Fact]
    public void TruncateLeavesShortEnoughTextAlone() =>
        Assert.Equal("Monday", PdfFontMetrics.Truncate("Monday", PdfFontFace.Regular, 8, 200));

    [Fact]
    public void TruncateFitsWithinTheWidthItWasGiven()
    {
        const double MaxWidth = 40;
        var result = PdfFontMetrics.Truncate("A rather long booking page title", PdfFontFace.Regular, 8, MaxWidth);

        Assert.EndsWith("...", result);
        Assert.True(PdfFontMetrics.Width(result, PdfFontFace.Regular, 8) <= MaxWidth);
    }

    [Fact]
    public void WrapBreaksOnWordsAndKeepsEveryLineInsideTheWidth()
    {
        const double MaxWidth = 60;
        var lines = PdfFontMetrics.Wrap("Booking confirmed for a guest today", PdfFontFace.Regular, 8, MaxWidth);

        Assert.True(lines.Count > 1);
        Assert.All(lines, l => Assert.True(PdfFontMetrics.Width(l, PdfFontFace.Regular, 8) <= MaxWidth, l));
    }
}

public class PdfDocumentBuilderTests
{
    [Fact]
    public void ProducesAValidSinglePageDocument()
    {
        var pdf = new PdfDocumentBuilder("Test");
        pdf.AddPage();
        pdf.Text(40, 60, "Hello", PdfFontFace.Regular, 12, "#000000");

        var bytes = pdf.Build();

        Assert.True(PdfInspector.IsStructurallyValid(bytes, out var reason), reason);
        Assert.Equal(1, PdfInspector.DeclaredPageCount(bytes));
        Assert.Equal(new[] { "Hello" }, PdfInspector.TextRuns(bytes));
    }

    [Fact]
    public void SelectPageLetsAnAlreadyLaidOutPageBeDrawnOnAgain()
    {
        var pdf = new PdfDocumentBuilder("Test");
        pdf.AddPage();
        pdf.Text(40, 60, "first", PdfFontFace.Regular, 10, "#000000");
        pdf.AddPage();
        pdf.Text(40, 60, "second", PdfFontFace.Regular, 10, "#000000");

        // This is what makes "Page 1 of 2" possible: the total is only known once
        // every page exists.
        pdf.SelectPage(0);
        pdf.Text(40, 800, "Page 1 of 2", PdfFontFace.Regular, 8, "#000000");

        var runs = PdfInspector.TextRuns(pdf.Build());

        Assert.Equal(new[] { "first", "Page 1 of 2", "second" }, runs);
    }

    [Fact]
    public void DeclaresBothBaseFontsWithWinAnsiEncodingAndEmbedsNeither()
    {
        var pdf = new PdfDocumentBuilder("Test");
        pdf.AddPage();
        var ascii = Encoding.ASCII.GetString(pdf.Build());

        Assert.Contains("/BaseFont /Helvetica /Encoding /WinAnsiEncoding", ascii, StringComparison.Ordinal);
        Assert.Contains("/BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding", ascii, StringComparison.Ordinal);
        Assert.DoesNotContain("/FontFile", ascii, StringComparison.Ordinal);
    }

    [Fact]
    public void ContentStreamLengthMatchesTheBytesActuallyWritten()
    {
        var pdf = new PdfDocumentBuilder("Test");
        pdf.AddPage();
        pdf.Text(40, 60, "café (30 min)", PdfFontFace.Bold, 11, "#2a78d6");
        pdf.Rect(10, 10, 100, 20, "#f6f6f3", "#e1e0d9");
        pdf.Line(10, 40, 110, 40, "#c3c2b7");
        pdf.Polyline([(0, 0), (10, 10), (20, 5)], "#eb6834");

        var bytes = pdf.Build();
        var ascii = Encoding.ASCII.GetString(bytes);

        // A /Length that disagrees with the stream is the classic way to produce
        // a file every reader rejects.
        var declared = int.Parse(System.Text.RegularExpressions.Regex.Match(ascii, @"<< /Length (\d+) >>").Groups[1].Value);
        var start = ascii.IndexOf("stream\n", StringComparison.Ordinal) + "stream\n".Length;
        var end = ascii.IndexOf("endstream", StringComparison.Ordinal);

        Assert.Equal(declared, end - start);
        Assert.True(PdfInspector.IsStructurallyValid(bytes, out var reason), reason);
    }

    [Fact]
    public void BuildsAnEmptyDocumentRatherThanThrowingWhenNothingWasDrawn()
    {
        var bytes = new PdfDocumentBuilder("Empty").Build();

        Assert.True(PdfInspector.IsStructurallyValid(bytes, out var reason), reason);
        Assert.Equal(1, PdfInspector.DeclaredPageCount(bytes));
    }
}
