namespace BookingTracker.Application.Analytics.Export.Pdf;

/// <summary>
/// Advance widths for the two base-14 faces the report uses, in 1/1000 em - the
/// unit Adobe's own AFM metrics for Helvetica and Helvetica-Bold are published
/// in, so a string's width here is the width the reader will lay out.
///
/// Without this the layout could not right-align a number, centre a heading,
/// truncate a long booking page title to fit its column, or wrap a paragraph:
/// all four need to know how wide text is *before* it is drawn, and a PDF
/// carries no layout engine of its own. Codes above 126 use the width of a
/// lowercase "o" as a stand-in; text is transliterated toward ASCII before it
/// gets here (see PdfTextEncoding), so that path is rare and only ever shifts a
/// truncation point by a fraction of a character.
/// </summary>
public static class PdfFontMetrics
{
    /// <summary>Cap height / ascent used to place a baseline under a given top edge, as a fraction of the font size.</summary>
    public const double Ascent = 0.72;

    private static readonly int[] Regular = BuildRegular();
    private static readonly int[] Bold = BuildBold();

    /// <summary>Width of <paramref name="text"/> in points, measured over the bytes it will actually be drawn as.</summary>
    public static double Width(string? text, PdfFontFace face, double fontSize)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        var widths = face == PdfFontFace.Bold ? Bold : Regular;
        var total = 0;
        foreach (var b in PdfTextEncoding.ToWinAnsi(text))
        {
            total += widths[b];
        }
        return total * fontSize / 1000.0;
    }

    /// <summary>
    /// The longest prefix of <paramref name="text"/> that fits in
    /// <paramref name="maxWidth"/>, with a trailing ellipsis when anything was
    /// dropped. A cell that silently overflows into its neighbour is worse than
    /// one that says it was cut.
    /// </summary>
    public static string Truncate(string text, PdfFontFace face, double fontSize, double maxWidth)
    {
        if (string.IsNullOrEmpty(text) || Width(text, face, fontSize) <= maxWidth) return text;

        const string Ellipsis = "...";
        var ellipsisWidth = Width(Ellipsis, face, fontSize);
        if (ellipsisWidth > maxWidth) return string.Empty;

        var kept = 0;
        var used = 0.0;
        var widths = face == PdfFontFace.Bold ? Bold : Regular;
        var bytes = PdfTextEncoding.ToWinAnsi(text);

        // ToWinAnsi is one byte per input char, so a byte index is a char index -
        // which is what lets the result be sliced off the original string and keep
        // any character the code page did represent.
        for (var i = 0; i < bytes.Length; i++)
        {
            var next = widths[bytes[i]] * fontSize / 1000.0;
            if (used + next + ellipsisWidth > maxWidth) break;
            used += next;
            kept++;
        }

        return text[..kept].TrimEnd() + Ellipsis;
    }

    /// <summary>Greedy word wrap at <paramref name="maxWidth"/>. A word longer than the line is broken rather than allowed to overflow.</summary>
    public static IReadOnlyList<string> Wrap(string text, PdfFontFace face, double fontSize, double maxWidth)
    {
        if (string.IsNullOrWhiteSpace(text)) return [string.Empty];

        var lines = new List<string>();
        var line = string.Empty;

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (Width(candidate, face, fontSize) <= maxWidth)
            {
                line = candidate;
                continue;
            }

            if (line.Length > 0) lines.Add(line);
            line = Width(word, face, fontSize) <= maxWidth ? word : Truncate(word, face, fontSize, maxWidth);
            if (line.EndsWith("...", StringComparison.Ordinal))
            {
                lines.Add(line);
                line = string.Empty;
            }
        }

        if (line.Length > 0) lines.Add(line);
        return lines.Count == 0 ? [string.Empty] : lines;
    }

    private static int[] BuildRegular()
    {
        // Adobe Helvetica AFM widths, indexed by WinAnsi code. "o" (556) fills
        // both the unused control range and codes above 126.
        var w = Fill(556);
        int[] ascii =
        [
            278, 278, 355, 556, 556, 889, 667, 222, 333, 333, 389, 584, 278, 333, 278, 278, // 32-47
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556, // 48-63
            1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778, // 64-79
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556, // 80-95
            222, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556, // 96-111
            556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,       // 112-126
        ];
        ascii.CopyTo(w, 32);
        return w;
    }

    private static int[] BuildBold()
    {
        var w = Fill(611);
        int[] ascii =
        [
            278, 333, 474, 556, 556, 889, 722, 278, 333, 333, 389, 584, 278, 333, 278, 278, // 32-47
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611, // 48-63
            975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778, // 64-79
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556, // 80-95
            278, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611, // 96-111
            611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,       // 112-126
        ];
        ascii.CopyTo(w, 32);
        return w;
    }

    private static int[] Fill(int value)
    {
        var w = new int[256];
        Array.Fill(w, value);
        return w;
    }
}
