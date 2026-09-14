using System.Text;
using System.Text.RegularExpressions;

namespace BookingTracker.UnitTests.TestSupport;

/// <summary>
/// Reads back a PDF this codebase produced, far enough to assert on it.
///
/// Not a general PDF parser - it only understands the subset
/// <see cref="Application.Analytics.Export.Pdf.PdfDocumentBuilder"/> emits
/// (uncompressed, ASCII-escaped content streams). That is deliberate: the point
/// is to verify the bytes actually carry the text and structure claimed, rather
/// than to assert that some byte array is non-empty and hope.
/// </summary>
public static partial class PdfInspector
{
    /// <summary>The visible text of the document, in draw order, one entry per Tj operator.</summary>
    public static IReadOnlyList<string> TextRuns(byte[] pdf)
    {
        var ascii = Encoding.ASCII.GetString(pdf);
        return TextOperator().Matches(ascii)
            .Select(m => Unescape(m.Groups[1].Value))
            .ToList();
    }

    public static string AllText(byte[] pdf) => string.Join("\n", TextRuns(pdf));

    /// <summary>Page count as the document itself declares it in the pages tree.</summary>
    public static int DeclaredPageCount(byte[] pdf)
    {
        var match = PageCount().Match(Encoding.ASCII.GetString(pdf));
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    /// <summary>Page objects actually present, which must agree with <see cref="DeclaredPageCount"/> or readers disagree about the document.</summary>
    public static int PageObjectCount(byte[] pdf) =>
        PageObject().Matches(Encoding.ASCII.GetString(pdf)).Count;

    /// <summary>
    /// The structural checks a reader performs before it renders anything: the
    /// header, a cross-reference table, a trailer naming the catalog, and a
    /// startxref pointing at a byte offset that really is where "xref" begins.
    /// The last one is the check that catches an off-by-one in the xref writer,
    /// which is otherwise invisible until a reader refuses the file.
    /// </summary>
    public static bool IsStructurallyValid(byte[] pdf, out string reason)
    {
        var ascii = Encoding.ASCII.GetString(pdf);

        if (!ascii.StartsWith("%PDF-1.", StringComparison.Ordinal)) { reason = "missing %PDF header"; return false; }
        if (!ascii.Contains("/Type /Catalog", StringComparison.Ordinal)) { reason = "missing catalog"; return false; }
        if (!ascii.Contains("trailer", StringComparison.Ordinal)) { reason = "missing trailer"; return false; }
        if (!ascii.TrimEnd().EndsWith("%%EOF", StringComparison.Ordinal)) { reason = "missing %%EOF"; return false; }

        var startXref = StartXref().Match(ascii);
        if (!startXref.Success) { reason = "missing startxref"; return false; }

        var offset = long.Parse(startXref.Groups[1].Value);
        if (offset <= 0 || offset >= pdf.Length) { reason = $"startxref {offset} outside the file"; return false; }
        if (!ascii.AsSpan((int)offset).StartsWith("xref")) { reason = $"startxref {offset} does not point at the xref table"; return false; }

        reason = string.Empty;
        return true;
    }

    /// <summary>Reverses PdfTextEncoding.ToPdfLiteral, so an assertion can be written against the string that was drawn.</summary>
    private static string Unescape(string literal)
    {
        var builder = new StringBuilder(literal.Length);
        for (var i = 0; i < literal.Length; i++)
        {
            if (literal[i] != '\\')
            {
                builder.Append(literal[i]);
                continue;
            }

            i++;
            if (i >= literal.Length) break;

            if (literal[i] is >= '0' and <= '7')
            {
                var octal = literal.Substring(i, Math.Min(3, literal.Length - i));
                builder.Append((char)Convert.ToInt32(octal, 8));
                i += octal.Length - 1;
            }
            else
            {
                builder.Append(literal[i]);
            }
        }
        return builder.ToString();
    }

    [GeneratedRegex(@"\(((?:\\.|[^()\\])*)\)\s*Tj", RegexOptions.Singleline)]
    private static partial Regex TextOperator();

    [GeneratedRegex(@"/Type /Pages /Kids \[[^\]]*\] /Count (\d+)")]
    private static partial Regex PageCount();

    [GeneratedRegex(@"/Type /Page /Parent")]
    private static partial Regex PageObject();

    [GeneratedRegex(@"startxref\s+(\d+)")]
    private static partial Regex StartXref();
}
