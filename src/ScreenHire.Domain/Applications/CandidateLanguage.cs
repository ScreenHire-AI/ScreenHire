using System.Globalization;
using System.Text.RegularExpressions;

namespace ScreenHire.Domain.Applications;

/// <summary>
/// The language a candidate's emails write dates in: the first <c>Accept-Language</c> tag of the browser they applied from,
/// recorded with the application (<see cref="JobApplication.Language"/>). A display preference only. Nothing reads it as a
/// signal about the candidate (CLAUDE.md non-negotiables 1 to 4). An application with none (added by a recruiter, or made
/// before this was recorded) is written in <see cref="DefaultTag"/>.
/// </summary>
public static partial class CandidateLanguage
{
    /// <summary>The language used when none was recorded or the host does not know it.</summary>
    public const string DefaultTag = "en-GB";

    /// <summary>The longest tag kept (the column's width).</summary>
    public const int MaxLength = 35;

    // The languages this host has data for. A well-formed tag for none of them ("xx-ZZ") would otherwise get a made-up
    // culture that formats like the invariant one.
    private static readonly Lazy<HashSet<string>> Known = new(
        () => new HashSet<string>(
            CultureInfo.GetCultures(CultureTypes.NeutralCultures).Select(c => c.TwoLetterISOLanguageName).Where(n => n.Length > 0),
            StringComparer.OrdinalIgnoreCase),
        LazyThreadSafetyMode.PublicationOnly);

    [GeneratedRegex("^[A-Za-z]{2,8}(-[A-Za-z0-9]{1,8}){0,3}$")]
    private static partial Regex TagShape();

    [GeneratedRegex(@"[,\s]*d{3,4}[,\s]*")]
    private static partial Regex Weekday();

    /// <summary>
    /// The tag to record for <paramref name="tag"/>: trimmed, when it has the shape of a language tag (a language and
    /// optional script, region and variants) and fits the column, otherwise null. Never a reason to refuse anything.
    /// </summary>
    public static string? Normalize(string? tag)
    {
        var trimmed = tag?.Trim();
        return trimmed is { Length: > 0 and <= MaxLength } && TagShape().IsMatch(trimmed) ? trimmed : null;
    }

    /// <summary>
    /// The culture to write <paramref name="tag"/>'s dates in: the tag itself, else its language, else en-GB. A language this
    /// host has no data for (or a tag that is not one) reads as en-GB, as does a host that runs without globalization data.
    /// </summary>
    public static CultureInfo Culture(string? tag)
    {
        if (Normalize(tag) is { } normalized)
        {
            var language = normalized.Split('-')[0].ToLowerInvariant();
            if (Known.Value.Contains(language))
            {
                foreach (var name in new[] { normalized, language })
                {
                    try
                    {
                        return CultureInfo.GetCultureInfo(name);
                    }
                    catch (CultureNotFoundException)
                    {
                        // Try the language alone, then the default.
                    }
                }
            }
        }

        try
        {
            return CultureInfo.GetCultureInfo(DefaultTag);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    /// <summary>
    /// A day as the culture writes it in full without the weekday: <c>29 November 2026</c> in en-GB, <c>November 29, 2026</c> in
    /// en-US. The date is <paramref name="instant"/>'s in UTC, as the careers page writes it.
    /// </summary>
    public static string LongDate(DateTimeOffset instant, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var pattern = Weekday().Replace(culture.DateTimeFormat.LongDatePattern, " ").Trim(' ', ',', '،');
        return instant.UtcDateTime.ToString(pattern.Length > 1 ? pattern : "D", culture);
    }

    /// <summary>
    /// The time of day as the culture writes it (<c>3:00 PM</c> in en-US, <c>15:00</c> in en-GB). Any no-break space a host's
    /// data puts before AM or PM becomes a plain one, so the text reads the same everywhere.
    /// </summary>
    public static string Clock(DateTimeOffset local, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return Plain(local.ToString(culture.DateTimeFormat.ShortTimePattern, culture));
    }

    /// <summary>The text with no-break spaces (U+00A0, U+202F) made plain.</summary>
    public static string Plain(string text) => text.Replace(' ', ' ').Replace(' ', ' ');
}
