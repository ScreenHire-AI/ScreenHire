using System.Globalization;
using ScreenHire.Domain.Verification;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.TalentPool;

namespace ScreenHire.Domain.History;

/// <summary>
/// The organization's settings changes in words, one history per settings page: the name ("renamed the organization
/// from Brightwater to <b>Brightwater Studio</b>") and the questions per page ("changed questions per page from 8 to
/// <b>10</b>") and the sites that may embed the careers page ("let <b>https://www.example.com</b> embed the careers
/// page"), all on the Organization page, the branding ("changed the brand colour from #1f4e5a to
/// <b>#2b2788</b>", "replaced the logo") and the verification settings ("turned on <b>the consistency check</b>"). Creating the organization is not
/// a change anyone made on these pages, so it records nothing.
/// </summary>
public static class SettingsChanges
{
    /// <param name="before">The organization before the save; null when the save added it.</param>
    /// <param name="after">The organization after the save.</param>
    public static IReadOnlyList<ChangeLine> Name(Organization? before, Organization? after)
    {
        if (before is null || after is null)
        {
            return [];
        }

        var lines = new List<ChangeLine>();
        if (before.Name != after.Name)
        {
            lines.Add(ChangeLine.Of($"renamed the organization from {before.Name} to ", after.Name));
        }

        if (before.QuestionsPerPage != after.QuestionsPerPage)
        {
            lines.Add(ChangeLine.Of(
                $"changed questions per page from {before.QuestionsPerPage.ToString(CultureInfo.InvariantCulture)} to ",
                after.QuestionsPerPage.ToString(CultureInfo.InvariantCulture)));
        }

        // The sites that may embed the careers pages, one line each, as on the Careers page panel.
        foreach (var origin in after.EmbedOrigins.Except(before.EmbedOrigins, StringComparer.Ordinal))
        {
            lines.Add(ChangeLine.Of("let ", origin, " embed the careers page"));
        }

        foreach (var origin in before.EmbedOrigins.Except(after.EmbedOrigins, StringComparer.Ordinal))
        {
            lines.Add(ChangeLine.Of("stopped ", origin, " embedding the careers page"));
        }

        return lines;
    }

    /// <param name="before">The organization before the save; null when the save added it.</param>
    /// <param name="after">The organization after the save.</param>
    public static IReadOnlyList<ChangeLine> Branding(Organization? before, Organization? after)
    {
        if (before is null || after is null)
        {
            return [];
        }

        var lines = new List<ChangeLine>();
        if (before.BrandColor != after.BrandColor)
        {
            lines.Add(after.BrandColor is null
                ? ChangeLine.Plain("went back to ScreenHire's default colour")
                : before.BrandColor is null
                    ? ChangeLine.Of("set the brand colour to ", after.BrandColor)
                    : ChangeLine.Of($"changed the brand colour from {before.BrandColor} to ", after.BrandColor));
        }

        if (before.SecondColor != after.SecondColor)
        {
            lines.Add(after.SecondColor is null
                ? ChangeLine.Plain("removed the second colour")
                : before.SecondColor is null
                    ? ChangeLine.Of("set the second colour to ", after.SecondColor)
                    : ChangeLine.Of($"changed the second colour from {before.SecondColor} to ", after.SecondColor));
        }

        if (before.HeadingFont != after.HeadingFont)
        {
            lines.Add(after.HeadingFont is { } font
                ? ChangeLine.Of("changed the heading font to ", font.Label())
                : ChangeLine.Plain("went back to ScreenHire's heading font"));
        }

        foreach (var kind in Enum.GetValues<BrandImageKind>())
        {
            var (was, now) = (before.ImageId(kind), after.ImageId(kind));
            if (was != now)
            {
                var noun = BrandImageRules.For(kind).Noun;
                lines.Add(ChangeLine.Plain(now is null ? $"removed the {noun}" : was is null ? $"added a {noun}" : $"replaced the {noun}"));
            }
        }

        return lines;
    }

    /// <param name="before">The settings before the save; null on the first save, which compares with the defaults.</param>
    /// <param name="after">The settings after the save.</param>
    public static IReadOnlyList<ChangeLine> Verification(VerificationSettings? before, VerificationSettings? after)
    {
        if (after is null)
        {
            return [];
        }

        var lines = new List<ChangeLine>();
        var wasOn = before?.LlmConsistencyCheckEnabled ?? false;
        if (wasOn != after.LlmConsistencyCheckEnabled)
        {
            lines.Add(ChangeLine.Of(after.LlmConsistencyCheckEnabled ? "turned on " : "turned off ", "the consistency check"));
        }

        var budget = before?.MonthlyTokenBudget;
        if (budget != after.MonthlyTokenBudget)
        {
            lines.Add(after.MonthlyTokenBudget is { } tokens
                ? ChangeLine.Of("set the monthly token budget to ", tokens.ToString("N0", CultureInfo.InvariantCulture))
                : ChangeLine.Plain("went back to ScreenHire's default token budget"));
        }

        return lines;
    }

    /// <param name="before">The settings before the save; null on the first save, which compares with the default.</param>
    /// <param name="after">The settings after the save.</param>
    public static IReadOnlyList<ChangeLine> TalentPool(TalentPoolSettings? before, TalentPoolSettings? after)
    {
        var was = TalentPoolSettings.MonthsOf(before);
        return after is null || was == after.PermissionMonths
            ? []
            : [ChangeLine.Of($"changed how long permission for future roles lasts from {Months(was)} to ", Months(after.PermissionMonths))];
    }

    private static string Months(int months) => string.Create(CultureInfo.InvariantCulture, $"{months} months");
}
