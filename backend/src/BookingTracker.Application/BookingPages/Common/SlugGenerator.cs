using System.Text.RegularExpressions;

namespace BookingTracker.Application.BookingPages.Common;

/// <summary>Turns a booking page title into a URL-safe slug base. Uniqueness (numeric suffixing) is resolved by the caller against the database.</summary>
public static partial class SlugGenerator
{
    private const int MaxLength = 80;

    public static string Slugify(string title)
    {
        var lowered = title.Trim().ToLowerInvariant();
        var slug = NonAlphanumericRun().Replace(lowered, "-").Trim('-');

        if (slug.Length > MaxLength)
            slug = slug[..MaxLength].Trim('-');

        return string.IsNullOrEmpty(slug) ? "booking-page" : slug;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumericRun();
}
