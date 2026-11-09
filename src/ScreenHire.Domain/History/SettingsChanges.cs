using System.Globalization;
using ScreenHire.Domain.Verification;
using ScreenHire.Domain.Organizations;
using ScreenHire.Domain.Privacy;
using ScreenHire.Domain.TalentPool;

namespace ScreenHire.Domain.History;

/// <summary>
/// The organization's settings changes in words, one history per settings page: the name ("renamed the organization
/// from Brightwater to <b>Brightwater Studio</b>") and the questions per page ("changed questions per page from 8 to
/// <b>10</b>") and the sites that may embed the careers page ("let <b>https://www.example.com</b> embed the careers
/// page"), all on the Organization page, the branding ("changed the brand colour from #1f4e5a to
/// <b>#2b2788</b>", "replaced the logo"), the verification settings ("turned on <b>the consistency check</b>") and record
/// retention ("turned on deleting ended applications <b>18 months</b> after they end"). Creating the organization is not
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
    public static IReadOnlyList<ChangeLine> Verification(VerificationSettings? before, VerificationSettings? after) =>
        Verification(before is null ? null : VerificationSettingsFacts.Of(before), after is null ? null : VerificationSettingsFacts.Of(after));

    /// <summary>
    /// The lines for a save, from the view History sees: it holds no key, only that one is saved and how often it was
    /// replaced, so a line can say "replaced the API key" and never show it.
    /// </summary>
    public static IReadOnlyList<ChangeLine> Verification(VerificationSettingsFacts? before, VerificationSettingsFacts? after)
    {
        if (after is null)
        {
            return [];
        }

        var was = before ?? VerificationSettingsFacts.Default;
        var lines = new List<ChangeLine>();
        if (was.ConsistencyCheck != after.ConsistencyCheck)
        {
            lines.Add(ChangeLine.Of(after.ConsistencyCheck ? "turned on " : "turned off ", "the consistency check"));
        }

        if (was.MonthlyTokenBudget != after.MonthlyTokenBudget)
        {
            lines.Add(after.MonthlyTokenBudget is { } tokens
                ? ChangeLine.Of("set the monthly token budget to ", tokens.ToString("N0", CultureInfo.InvariantCulture))
                : ChangeLine.Plain("went back to ScreenHire's default token budget"));
        }

        if (was.Source != after.Source)
        {
            lines.Add(after.Source == LlmSource.OwnKey
                ? ChangeLine.Of("switched to ", "your own AI key")
                : ChangeLine.Of("switched to ", "ScreenHire's included AI model"));
        }

        if (after.Source == LlmSource.OwnKey)
        {
            if (was.Provider != after.Provider && after.Provider is { } provider)
            {
                lines.Add(ChangeLine.Of("set the AI provider to ", ChangeLabels.ModelProvider(provider)));
            }

            if (was.Host != after.Host && after.Host is { } host)
            {
                lines.Add(ChangeLine.Of("set the AI endpoint to ", host));
            }

            if (was.Model != after.Model && after.Model is { } model)
            {
                lines.Add(ChangeLine.Of("set the AI model to ", model));
            }
        }

        // The key itself is never in a line. Replacing it with the same text still counts: the version moved.
        if (!was.HasApiKey && after.HasApiKey)
        {
            lines.Add(ChangeLine.Plain("saved an API key"));
        }
        else if (was.HasApiKey && !after.HasApiKey)
        {
            lines.Add(ChangeLine.Plain("removed the API key"));
        }
        else if (after.HasApiKey && was.ApiKeyVersion != after.ApiKeyVersion)
        {
            lines.Add(ChangeLine.Plain("replaced the API key"));
        }

        foreach (var group in Enum.GetValues<CheckGroup>())
        {
            if (was.Runs(group) != after.Runs(group))
            {
                lines.Add(ChangeLine.Of(after.Runs(group) ? "turned on " : "turned off ", $"{CheckGroups.Label(group).ToLowerInvariant()} checks"));
            }
        }

        if (was.DefaultAiUse != after.DefaultAiUse)
        {
            lines.Add(ChangeLine.Of("set the AI use policy to ", ChangeLabels.AiUse(after.DefaultAiUse)));
        }

        if (was.DeviceCheck != after.DeviceCheck)
        {
            lines.Add(ChangeLine.Of("set the device check to ", ChangeLabels.DeviceCheck(after.DeviceCheck)));
        }

        AddTrusted(lines, was.TrustedDomains, after.TrustedDomains, "domain");
        AddTrusted(lines, was.TrustedNetworks, after.TrustedNetworks, "network");

        AddAlert(lines, was.SuggestedAlertEmail, after.SuggestedAlertEmail, "the Verification suggested alert by email");
        AddAlert(lines, was.SuggestedAlertSlack, after.SuggestedAlertSlack, "the Verification suggested alert on Slack");
        AddAlert(lines, was.BudgetAlertEmail, after.BudgetAlertEmail, "the budget alert by email");
        AddAlert(lines, was.BudgetAlertSlack, after.BudgetAlertSlack, "the budget alert on Slack");
        return lines;
    }

    private static void AddTrusted(List<ChangeLine> lines, IReadOnlyList<string> was, IReadOnlyList<string> now, string kind)
    {
        lines.AddRange(now.Except(was, StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(entry => ChangeLine.Of($"trusted the {kind} ", entry)));
        lines.AddRange(was.Except(now, StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(entry => ChangeLine.Of($"stopped trusting the {kind} ", entry)));
    }

    private static void AddAlert(List<ChangeLine> lines, bool was, bool now, string name)
    {
        if (was != now)
        {
            lines.Add(ChangeLine.Of(now ? "turned on " : "turned off ", name));
        }
    }

    /// <param name="before">The settings before the save; null on the first save, which compares with the default (off).</param>
    /// <param name="after">The settings after the save.</param>
    public static IReadOnlyList<ChangeLine> Retention(RetentionSettings? before, RetentionSettings? after)
    {
        if (after is null)
        {
            return [];
        }

        var (was, now) = (before?.EndedApplicationsMonths, after.EndedApplicationsMonths);
        if (was == now)
        {
            return [];
        }

        if (now is not { } months)
        {
            return [ChangeLine.Of("turned off ", "deleting ended applications")];
        }

        var period = $"{months.ToString(CultureInfo.InvariantCulture)} {(months == 1 ? "month" : "months")}";
        return was is null
            ? [ChangeLine.Of("turned on deleting ended applications ", period, " after they end")]
            : [ChangeLine.Of("changed when ended applications are deleted to ", period, " after they end")];
    }

    /// <param name="before">The saved entry before the save; null when the save added it.</param>
    /// <param name="after">The saved entry after the save; null when the save removed it.</param>
    public static IReadOnlyList<ChangeLine> SavedNote(TalentPoolEntry? before, TalentPoolEntry? after)
    {
        // Saving someone and taking them out are on the candidate's Activity; only an edit of the note is a change here.
        if (before is null || after is null || before.Note == after.Note)
        {
            return [];
        }

        var lead = after.Note is null ? "removed the note" : before.Note is null ? "added a note" : "edited the note";
        return [ChangeLine.Plain(lead, [new ChangeDetail("Note", before.Note, after.Note, ChangeValueKind.Paragraphs)])];
    }

    /// <summary>
    /// What happened to the organization's sending domain: added, verified by the provider, no longer verified, or removed.
    /// A check that only refreshes the records' states, or replaces them with the same facts, says nothing: history is for
    /// what a person would ask "who did that" about, not for every look-up.
    /// </summary>
    public static IReadOnlyList<ChangeLine> SendingDomain(SendingDomain? before, SendingDomain? after)
    {
        if (before is null && after is null)
        {
            return [];
        }

        if (before is null)
        {
            return [ChangeLine.Of("added the sending domain ", after!.Domain)];
        }

        if (after is null)
        {
            return [ChangeLine.Of("removed the sending domain ", before.Domain)];
        }

        if (before.Status != SendingDomainStatus.Verified && after.Status == SendingDomainStatus.Verified)
        {
            return [ChangeLine.Of("verified the sending domain ", after.Domain, ": email now comes from it")];
        }

        return before.Status == SendingDomainStatus.Verified && after.Status != SendingDomainStatus.Verified
            ? [ChangeLine.Of("found the sending domain ", after.Domain, " no longer verified: email comes from ScreenHire again")]
            : [];
    }
}
