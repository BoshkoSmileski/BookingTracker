using System.Globalization;
using System.Text;

namespace BookingTracker.Application.Analytics.Export.Pdf;

/// <summary>
/// Turns .NET strings into the bytes a PDF literal string can carry.
///
/// The report's fonts are the base-14 standard faces, declared with
/// /WinAnsiEncoding - a single-byte encoding equivalent to Windows-1252. That
/// buys "no embedded font, opens everywhere, tiny file" at the price of a
/// character repertoire: Latin-1 plus the usual typographic extras, nothing
/// beyond. Rather than emit a broken glyph, text is degraded deliberately:
/// 1. a character Windows-1252 has is used directly;
/// 2. otherwise the character is decomposed and its combining marks dropped, so
///    an accented Latin letter survives as its base letter - which covers most
///    Latin-script names;
/// 3. anything still unrepresentable (Cyrillic, Greek, CJK) becomes "?".
/// The CSV export is UTF-8 and carries such text intact; embedding a Unicode
/// TrueType font would remove the limit here and is a listed extension point.
///
/// The code page is built in this file rather than taken from
/// CodePagesEncodingProvider, because Windows-1252 is not in .NET's default
/// encoding set on non-Windows hosts and an export must not produce different
/// bytes depending on where the API happens to run.
///
/// Escape sequences are used for every non-ASCII literal below on purpose: this
/// file defines a byte-level mapping, and a source character that looks like a
/// space but is not one would be a silent correctness bug.
/// </summary>
public static class PdfTextEncoding
{
    private const byte Replacement = (byte)'?';

    /// <summary>The 0x80-0x9F block, the only place Windows-1252 differs from ISO-8859-1. Keys are the Unicode characters those codes actually mean.</summary>
    private static readonly Dictionary<char, byte> HighRange = new()
    {
        [(char)0x20AC] = 0x80, // euro
        [(char)0x201A] = 0x82, // single low quote
        [(char)0x0192] = 0x83, // florin
        [(char)0x201E] = 0x84, // double low quote
        [(char)0x2026] = 0x85, // ellipsis
        [(char)0x2020] = 0x86, // dagger
        [(char)0x2021] = 0x87, // double dagger
        [(char)0x02C6] = 0x88, // circumflex
        [(char)0x2030] = 0x89, // per mille
        [(char)0x0160] = 0x8A, // S caron
        [(char)0x2039] = 0x8B, // single left angle quote
        [(char)0x0152] = 0x8C, // OE
        [(char)0x017D] = 0x8E, // Z caron
        [(char)0x2018] = 0x91, // left single quote
        [(char)0x2019] = 0x92, // right single quote
        [(char)0x201C] = 0x93, // left double quote
        [(char)0x201D] = 0x94, // right double quote
        [(char)0x2022] = 0x95, // bullet
        [(char)0x2013] = 0x96, // en dash
        [(char)0x2014] = 0x97, // em dash
        [(char)0x02DC] = 0x98, // small tilde
        [(char)0x2122] = 0x99, // trademark
        [(char)0x0161] = 0x9A, // s caron
        [(char)0x203A] = 0x9B, // single right angle quote
        [(char)0x0153] = 0x9C, // oe
        [(char)0x017E] = 0x9E, // z caron
        [(char)0x0178] = 0x9F, // Y diaeresis
    };

    /// <summary>The single-byte codes the given text becomes. Layout measurement works off this too, so what is measured is exactly what is drawn.</summary>
    public static byte[] ToWinAnsi(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];

        var bytes = new List<byte>(text.Length);
        foreach (var ch in text)
        {
            if (TryEncode(ch, out var encoded))
            {
                bytes.Add(encoded);
                continue;
            }

            // Strip diacritics and retry, so a name the code page cannot spell
            // exactly still reads as a name rather than a row of question marks.
            var stripped = StripMarks(ch);
            bytes.Add(stripped is { } baseChar && TryEncode(baseChar, out var fallback) ? fallback : Replacement);
        }

        return [.. bytes];
    }

    /// <summary>
    /// A PDF literal string body, ASCII-only. Backslash, both parentheses and
    /// anything outside printable ASCII are escaped (the last as octal), so the
    /// content stream never carries a raw byte that could terminate the string
    /// early or trip a parser.
    /// </summary>
    public static string ToPdfLiteral(string? text)
    {
        var builder = new StringBuilder();
        foreach (var b in ToWinAnsi(text))
        {
            switch (b)
            {
                case (byte)'\\': builder.Append("\\\\"); break;
                case (byte)'(': builder.Append("\\("); break;
                case (byte)')': builder.Append("\\)"); break;
                default:
                    if (b is < 32 or > 126)
                    {
                        builder.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
                    }
                    else
                    {
                        builder.Append((char)b);
                    }
                    break;
            }
        }
        return builder.ToString();
    }

    private static bool TryEncode(char ch, out byte value)
    {
        // Printable ASCII (U+0020-U+007E), then the Latin-1 supplement
        // (U+00A0-U+00FF), which Windows-1252 shares with ISO-8859-1
        // code-for-code. U+0080-U+009F are C1 controls in Unicode and are
        // deliberately not in either range - Windows-1252 puts typographic
        // characters at those codes instead, which HighRange maps explicitly.
        if (ch is (>= ' ' and <= '~') or (>= (char)0x00A0 and <= (char)0x00FF))
        {
            value = (byte)ch;
            return true;
        }

        if (HighRange.TryGetValue(ch, out value)) return true;

        value = Replacement;
        return false;
    }

    /// <summary>The base letter of a precomposed character, or null when the character has no Latin base (Cyrillic, CJK, symbols).</summary>
    private static char? StripMarks(char ch)
    {
        var decomposed = ch.ToString().Normalize(NormalizationForm.FormD);
        foreach (var part in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(part) != UnicodeCategory.NonSpacingMark) return part;
        }
        return null;
    }
}
