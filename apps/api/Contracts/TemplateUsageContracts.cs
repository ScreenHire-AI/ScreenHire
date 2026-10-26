namespace ScreenHire.Api.Contracts;

/// <summary>What uses a template: a job, a hiring template, or an automation in either.</summary>
public enum UsageKind
{
    Job,
    HiringTemplate,
    JobAutomation,
    TemplateAutomation,
}

/// <summary>
/// One thing that uses a template. <c>id</c> is the job's or the hiring template's id; for an automation it is the job
/// or template the automation belongs to, and <c>automationId</c> and <c>automationName</c> name the automation.
/// </summary>
public sealed record UsageItem(UsageKind Kind, Guid Id, string Name, string? AutomationId = null, string? AutomationName = null);

/// <summary>
/// Where one template is used and when it was last used. <c>templateId</c> is the id its own list gives it.
/// <c>usedBy</c> lists every user, so its length is the count: jobs that are not closed for a hiring template, hiring
/// templates for a scorecard, automations for an email. <c>lastUsedAt</c>: when a job last started from it, a stage last
/// used it or an email last went out from it; null when never. <c>notUsedRecently</c>: nothing used it in the last 180
/// days, and the team has had it longer than that.
/// </summary>
public sealed record TemplateUse(string TemplateId, IReadOnlyList<UsageItem> UsedBy, DateTimeOffset? LastUsedAt, bool NotUsedRecently);

/// <summary>Every template's use, by kind, as the Templates page lists them.</summary>
public sealed record TemplateUsageList(IReadOnlyList<TemplateUse> Hiring, IReadOnlyList<TemplateUse> Scorecards, IReadOnlyList<TemplateUse> Emails);
