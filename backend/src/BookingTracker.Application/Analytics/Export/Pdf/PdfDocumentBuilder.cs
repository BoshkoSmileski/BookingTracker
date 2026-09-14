using System.Globalization;
using System.Text;

namespace BookingTracker.Application.Analytics.Export.Pdf;

/// <summary>
/// A minimal PDF 1.4 writer: pages, text, rectangles and lines, assembled into a
/// valid cross-referenced document. BCL only - no PDF package.
///
/// This is the same call the ICS invitation made: the hard parts of the format at this scope - object numbering,
/// a byte-exact xref table, content-stream operators, and text metrics - are a
/// few hundred lines and are covered by tests, whereas a PDF library would be by
/// some distance the largest dependency in a project that otherwise runs on the
/// BCL plus EF Core, MediatR and the Google SDK. It also sidesteps the licence
/// question the popular .NET options carry.
///
/// What it deliberately does NOT do: images, embedded fonts, transparency,
/// annotations, or compressed streams. The report needs none of them, and each
/// would be real format surface to get wrong.
///
/// Coordinates are top-left origin with y growing downward, because that is how
/// the layout code thinks; the conversion to PDF's bottom-left origin happens
/// here, once. A text y is its **baseline**.
/// </summary>
public sealed class PdfDocumentBuilder
{
    public const double A4Width = 595.28;
    public const double A4Height = 841.89;

    private const string RegularFontResource = "F1";
    private const string BoldFontResource = "F2";

    private readonly List<StringBuilder> _pages = [];
    private int _current = -1;

    public PdfDocumentBuilder(string title, double pageWidth = A4Width, double pageHeight = A4Height)
    {
        Title = title;
        PageWidth = pageWidth;
        PageHeight = pageHeight;
    }

    public string Title { get; }
    public double PageWidth { get; }
    public double PageHeight { get; }
    public int PageCount => _pages.Count;

    /// <summary>Appends a page and makes it current. Returns its zero-based index.</summary>
    public int AddPage()
    {
        _pages.Add(new StringBuilder());
        _current = _pages.Count - 1;
        return _current;
    }

    /// <summary>
    /// Re-selects an existing page so it can be drawn on again. This is what makes
    /// "Page 3 of 7" possible: the total is unknown until the last page exists, so
    /// footers are stamped in a second pass over pages already laid out.
    /// </summary>
    public void SelectPage(int index)
    {
        if (index < 0 || index >= _pages.Count) throw new ArgumentOutOfRangeException(nameof(index));
        _current = index;
    }

    public void Text(double x, double baselineY, string? text, PdfFontFace face, double fontSize, string colorHex)
    {
        if (string.IsNullOrEmpty(text)) return;

        var resource = face == PdfFontFace.Bold ? BoldFontResource : RegularFontResource;
        Content
            .Append("BT /").Append(resource).Append(' ').Append(Num(fontSize)).Append(" Tf ")
            .Append(Fill(colorHex))
            .Append(Num(x)).Append(' ').Append(Num(PageHeight - baselineY)).Append(" Td (")
            .Append(PdfTextEncoding.ToPdfLiteral(text))
            .Append(") Tj ET\n");
    }

    public void TextRight(double right, double baselineY, string? text, PdfFontFace face, double fontSize, string colorHex) =>
        Text(right - PdfFontMetrics.Width(text, face, fontSize), baselineY, text, face, fontSize, colorHex);

    public void TextCenter(double center, double baselineY, string? text, PdfFontFace face, double fontSize, string colorHex) =>
        Text(center - (PdfFontMetrics.Width(text, face, fontSize) / 2), baselineY, text, face, fontSize, colorHex);

    /// <summary><paramref name="top"/> is the rectangle's upper edge; it grows downward by <paramref name="height"/>.</summary>
    public void Rect(double x, double top, double width, double height, string? fillHex, string? strokeHex = null, double lineWidth = 0.6)
    {
        if (width <= 0 || height <= 0 || (fillHex is null && strokeHex is null)) return;

        var bottom = PageHeight - top - height;
        if (fillHex is not null) Content.Append(Fill(fillHex));
        if (strokeHex is not null) Content.Append(Stroke(strokeHex)).Append(Num(lineWidth)).Append(" w ");

        Content
            .Append(Num(x)).Append(' ').Append(Num(bottom)).Append(' ')
            .Append(Num(width)).Append(' ').Append(Num(height)).Append(" re ")
            .Append(PaintOperator(fillHex, strokeHex))
            .Append('\n');
    }

    public void Line(double x1, double y1, double x2, double y2, string colorHex, double lineWidth = 0.6)
    {
        Content
            .Append(Stroke(colorHex)).Append(Num(lineWidth)).Append(" w ")
            .Append(Num(x1)).Append(' ').Append(Num(PageHeight - y1)).Append(" m ")
            .Append(Num(x2)).Append(' ').Append(Num(PageHeight - y2)).Append(" l S\n");
    }

    /// <summary>An open polyline - one stroked path for a whole data series, rather than a segment per point.</summary>
    public void Polyline(IReadOnlyList<(double X, double Y)> points, string colorHex, double lineWidth = 1.4)
    {
        if (points.Count < 2) return;

        Content.Append(Stroke(colorHex)).Append(Num(lineWidth)).Append(" w 1 J 1 j ");
        for (var i = 0; i < points.Count; i++)
        {
            Content
                .Append(Num(points[i].X)).Append(' ').Append(Num(PageHeight - points[i].Y))
                .Append(i == 0 ? " m " : " l ");
        }
        Content.Append("S\n");
    }

    /// <summary>Assembles the document: header, objects, xref table, trailer.</summary>
    public byte[] Build()
    {
        if (_pages.Count == 0) AddPage();

        // Fixed low object numbers, then two objects per page (the page and its
        // content stream), so an object number can be computed rather than tracked.
        const int Catalog = 1, PagesNode = 2, RegularFont = 3, BoldFont = 4, Info = 5;
        var firstPageObject = 6;
        var totalObjects = firstPageObject + (_pages.Count * 2) - 1;

        var offsets = new long[totalObjects + 1];

        using var stream = new MemoryStream();
        void WriteAscii(string s) => stream.Write(Encoding.ASCII.GetBytes(s));

        WriteAscii("%PDF-1.4\n");
        // A comment line of high bytes marks the file as binary, so naive tools
        // transfer it verbatim instead of "helpfully" translating line endings.
        stream.Write([(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);

        void WriteObject(int number, string content)
        {
            offsets[number] = stream.Position;
            WriteAscii($"{number} 0 obj\n{content}\nendobj\n");
        }

        var kids = string.Join(' ', Enumerable.Range(0, _pages.Count).Select(i => $"{firstPageObject + (i * 2)} 0 R"));

        WriteObject(Catalog, $"<< /Type /Catalog /Pages {PagesNode} 0 R >>");
        WriteObject(PagesNode, $"<< /Type /Pages /Kids [{kids}] /Count {_pages.Count} >>");
        WriteObject(RegularFont, FontObject("Helvetica"));
        WriteObject(BoldFont, FontObject("Helvetica-Bold"));
        WriteObject(Info,
            "<< /Producer (BookingTracker) /Creator (BookingTracker Analytics) " +
            $"/Title ({PdfTextEncoding.ToPdfLiteral(Title)}) /CreationDate ({PdfDate(DateTime.UtcNow)}) >>");

        for (var i = 0; i < _pages.Count; i++)
        {
            var pageNumber = firstPageObject + (i * 2);
            var contentNumber = pageNumber + 1;
            var content = _pages[i].ToString();
            // Ascii is right: every byte a content stream can contain has already
            // been escaped to printable ASCII by PdfTextEncoding.
            var contentBytes = Encoding.ASCII.GetByteCount(content);

            WriteObject(pageNumber,
                $"<< /Type /Page /Parent {PagesNode} 0 R /MediaBox [0 0 {Num(PageWidth)} {Num(PageHeight)}] " +
                $"/Resources << /Font << /{RegularFontResource} {RegularFont} 0 R /{BoldFontResource} {BoldFont} 0 R >> >> " +
                $"/Contents {contentNumber} 0 R >>");
            WriteObject(contentNumber, $"<< /Length {contentBytes} >>\nstream\n{content}endstream");
        }

        var xrefOffset = stream.Position;
        // Every xref entry is exactly 20 bytes; a reader seeks by arithmetic, so a
        // byte off here makes the file unopenable rather than merely ugly.
        WriteAscii($"xref\n0 {totalObjects + 1}\n");
        WriteAscii("0000000000 65535 f \n");
        for (var i = 1; i <= totalObjects; i++)
        {
            WriteAscii($"{offsets[i]:D10} 00000 n \n");
        }

        WriteAscii(
            $"trailer\n<< /Size {totalObjects + 1} /Root {Catalog} 0 R /Info {Info} 0 R >>\n" +
            $"startxref\n{xrefOffset}\n%%EOF\n");

        return stream.ToArray();
    }

    /// <summary>Drawing before the first AddPage() creates one rather than throwing - a caller should not have to open a document to draw on it.</summary>
    private StringBuilder Content => _pages[_current >= 0 ? _current : AddPage()];

    private static string FontObject(string baseFont) =>
        $"<< /Type /Font /Subtype /Type1 /BaseFont /{baseFont} /Encoding /WinAnsiEncoding >>";

    private static string PdfDate(DateTime utc) =>
        utc.ToString("'D:'yyyyMMddHHmmss'Z'", CultureInfo.InvariantCulture);

    private static string PaintOperator(string? fill, string? stroke) =>
        fill is not null && stroke is not null ? "B" : fill is not null ? "f" : "S";

    private static string Fill(string hex)
    {
        var (r, g, b) = ParseColor(hex);
        return $"{Num(r)} {Num(g)} {Num(b)} rg ";
    }

    private static string Stroke(string hex)
    {
        var (r, g, b) = ParseColor(hex);
        return $"{Num(r)} {Num(g)} {Num(b)} RG ";
    }

    private static (double R, double G, double B) ParseColor(string hex)
    {
        var value = hex.AsSpan().TrimStart('#');
        if (value.Length != 6) return (0, 0, 0);
        return (
            int.Parse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0,
            int.Parse(value[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0,
            int.Parse(value[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0);
    }

    /// <summary>Invariant, no exponent, no trailing zeroes - a PDF number is not locale-aware and "0,5" is a syntax error.</summary>
    private static string Num(double value) => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);
}
