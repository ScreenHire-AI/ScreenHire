namespace ScreenHire.Domain.Hiring;

/// <summary>
/// What a workflow uses from the template library, for the Templates page's Used by and "Not used in 6 months"
/// (docs/design/design-system.md section 22). Pure: the API loads the workflows and the times, and asks these.
/// </summary>
public static class TemplateUses
{
    /// <summary>How far back "recently" reaches: a template nothing used in this many days is "not used in 6 months".</summary>
    public const int RecentDays = 180;

    /// <summary>
    /// The email templates a workflow's automations send, one entry per automation and template. A <see cref="ActionType.Reject"/>
    /// action always sends the rejection email and a <see cref="ActionType.SendQuestionnaire"/> action the follow-up
    /// questions email, though neither names it, so they count as uses of those built-ins.
    /// </summary>
    public static IReadOnlyList<EmailUse> EmailUses(HiringWorkflow workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var uses = new List<EmailUse>();
        foreach (var automation in workflow.Automations ?? [])
        {
            if (automation is null)
            {
                continue;
            }

            var keys = (automation.Actions ?? [])
                .Select(action => action?.Type switch
                {
                    ActionType.SendEmail => action.EmailTemplate,
                    ActionType.Reject => EmailTemplateIds.ApplicationRejected,
                    ActionType.SendQuestionnaire => EmailTemplateIds.QuestionnaireRequest,
                    _ => null,
                })
                .OfType<string>()
                .Distinct(StringComparer.Ordinal);
            uses.AddRange(keys.Select(key => new EmailUse(key, automation.Id, automation.Name)));
        }

        return uses;
    }

    /// <summary>
    /// Whether nothing used a template in the last <see cref="RecentDays"/> days: <paramref name="lastUsedAt"/> is
    /// unknown or older. A template the team has had for less than that, counted from <paramref name="since"/>, has not
    /// had the chance, so it is never "not used".
    /// </summary>
    public static bool NotUsedRecently(DateTimeOffset? lastUsedAt, DateTimeOffset since, DateTimeOffset now)
    {
        var cutoff = now.AddDays(-RecentDays);
        return since <= cutoff && (lastUsedAt is null || lastUsedAt <= cutoff);
    }
}

/// <summary>One automation sending one email template, by its key (a built-in id or an own template's id).</summary>
public sealed record EmailUse(string EmailTemplate, string AutomationId, string AutomationName);
