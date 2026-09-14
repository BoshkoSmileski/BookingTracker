using System.Globalization;
using System.Text;
using BookingTracker.Application.Analytics.Dtos;

namespace BookingTracker.Application.Analytics.Export;

/// <summary>
/// Names an export file after the filter that produced it, so a folder of
/// downloads stays self-describing rather than becoming "analytics (3).csv".
///
/// The result is ASCII, lower-case and free of path separators: it ends up in a
/// Content-Disposition header and then in a file system, and neither is a good
/// place to discover that a booking page title contained a quote or a slash.
/// </summary>
public static class AnalyticsExportFileName
{
    private const int MaxSlugLength = 40;

    public static string For(AnalyticsReportMetaDto meta, string extension)
    {
        var builder = new StringBuilder("bookingtracker-analytics");

        if (meta.BookingPageLabel != "All booking pages")
        {
            var slug = Slugify(meta.BookingPageLabel);
            if (slug.Length > 0) builder.Append('-').Append(slug);
        }

        if (meta.From is { } from) builder.Append('-').Append(from.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        if (meta.To is { } to) builder.Append("-to-").Append(to.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        if (meta.From is null && meta.To is null) builder.Append("-all-time");

        return builder.Append('.').Append(extension).ToString();
    }

    /// <summary>Keeps letters and digits, turns everything else into a single hyphen. Anything non-ASCII is dropped rather than transliterated - a file name is not the place to be clever.</summary>
    private static string Slugify(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingHyphen = false;

        foreach (var ch in value)
        {
            if (ch is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                if (pendingHyphen && builder.Length > 0) builder.Append('-');
                pendingHyphen = false;
                builder.Append(ch);
            }
            else if (ch is >= 'A' and <= 'Z')
            {
                if (pendingHyphen && builder.Length > 0) builder.Append('-');
                pendingHyphen = false;
                builder.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                pendingHyphen = true;
            }

            if (builder.Length >= MaxSlugLength) break;
        }

        return builder.ToString();
    }
}
