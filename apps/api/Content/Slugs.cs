using System.Globalization;
using System.Text;

namespace ScreenHire.Api.Content;

/// <summary>URL slugs made from titles: lowercase ASCII letters and digits separated by single hyphens.</summary>
internal static class Slugs
{
    /// <summary>Leaves room under the 120-character column for a "-2" style suffix.</summary>
    public const int MaxBaseLength = 100;

    public static string FromTitle(string title)
    {
        var builder = new StringBuilder(title.Length);
        var pendingHyphen = false;
        foreach (var c in title.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue; // é becomes e
            }

            var lower = char.ToLowerInvariant(c);
            if (lower is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                if (pendingHyphen && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(lower);
                pendingHyphen = false;
            }
            else
            {
                pendingHyphen = true;
            }
        }

        var slug = builder.Length > MaxBaseLength ? builder.ToString(0, MaxBaseLength).TrimEnd('-') : builder.ToString();
        return slug.Length == 0 ? "job" : slug;
    }

    /// <summary><paramref name="slug"/>, or the first of slug-2, slug-3 and so on that <paramref name="taken"/> lacks.</summary>
    public static string FirstFree(string slug, IReadOnlySet<string> taken)
    {
        if (!taken.Contains(slug))
        {
            return slug;
        }

        for (var n = 2; ; n++)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{slug}-{n}");
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
